using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>A row in the rule tree editor: a condition or a group of rows. Reads back to <see cref="RuleSettings"/>.</summary>
internal abstract class RuleNode : FlowLayoutPanel
{
	protected RuleNode()
	{
		FlowDirection = FlowDirection.TopDown;
		WrapContents = false;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		Margin = new Padding(0, 0, 0, 2);
	}

	public event EventHandler? Edited;

	public event EventHandler? RemoveRequested;

	public abstract RuleSettings ToSettings();

	protected void RaiseEdited() => Edited?.Invoke(this, EventArgs.Empty);

	protected void RaiseRemove() => RemoveRequested?.Invoke(this, EventArgs.Empty);

	public static RuleNode From(RuleSettings settings)
		=> settings switch
		{
			RuleSettings.Group group => new GroupNode(group.Combinator, group.Children, isRoot: false),
			RuleSettings.Condition condition => new ConditionNode(condition),
			_ => throw new ArgumentOutOfRangeException(nameof(settings)),
		};
}

/// <summary>A flow panel that draws a guide line down its left edge, so nested groups read as nested.</summary>
internal sealed class IndentPanel : FlowLayoutPanel
{
	public IndentPanel()
	{
		FlowDirection = FlowDirection.TopDown;
		WrapContents = false;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		Padding = new Padding(22, 0, 0, 0);
		Margin = new Padding(0);
		ResizeRedraw = true;
	}

	public bool DrawGuide { get; set; } = true;

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		if (!DrawGuide)
		{
			return;
		}
		using var pen = new Pen(SystemColors.ControlDark);
		e.Graphics.DrawLine(pen, 9, 2, 9, Height - 4);
	}
}

internal sealed class ConditionNode : RuleNode
{
	private static readonly string[] YesNoWords = ["Yes", "No", "Unknown"];
	private static readonly string[] YesNoStored = ["Yes", "No", "Unknown", "YesAndRightToLeft"];
	private const string YesRightToLeftWord = "Yes (right to left)";

	private static readonly Field[] RuleFields = [.. Fields.All.Where(f => f.Uses.HasFlag(FieldUses.Rule)).OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

	private readonly ComboBox fieldBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(0, 1, 6, 1), FormattingEnabled = true };
	private readonly ComboBox operatorBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(0, 1, 6, 1), FormattingEnabled = true };
	private readonly TextBox valueText = new() { Width = 220, Margin = new Padding(0, 1, 6, 1) };
	private readonly ComboBox valueChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(0, 1, 6, 1) };
	private readonly Label error = new() { AutoSize = true, ForeColor = Ui.ErrorColor, Margin = new Padding(0, 0, 0, 2), MaximumSize = new Size(600, 0), Visible = false };
	private readonly string? unknownKey;
	private string rawChoice = "";
	private int loading;

	public ConditionNode(RuleSettings.Condition condition)
	{
		Field? field = Fields.ByKey(condition.FieldKey);
		if (field is null || !field.Uses.HasFlag(FieldUses.Rule))
		{
			unknownKey = condition.FieldKey;
			fieldBox.Items.Add("(unknown) " + condition.FieldKey);
		}
		fieldBox.Items.AddRange([.. RuleFields]);
		fieldBox.Format += (_, e) => e.Value = e.Value is Field f ? f.DisplayName : e.Value;
		operatorBox.Format += (_, e) => e.Value = e.Value is Operator op ? Operators.Describe(op) : e.Value;

		loading++;
		fieldBox.SelectedItem = field is not null && field.Uses.HasFlag(FieldUses.Rule) ? field : fieldBox.Items[0];
		FillOperators(condition.Operator);
		SetValue(condition.RawOperand);
		loading--;

		var remove = Ui.Button("Remove", (_, _) => RaiseRemove());
		remove.Margin = new Padding(0, 0, 0, 0);
		var row = Ui.Row(fieldBox, operatorBox, valueText, valueChoice, remove);
		Controls.Add(row);
		Controls.Add(error);

		fieldBox.SelectedIndexChanged += (_, _) => OnFieldChanged();
		operatorBox.SelectedIndexChanged += (_, _) => OnOperatorChanged();
		valueText.TextChanged += (_, _) => Changed();
		valueChoice.SelectedIndexChanged += (_, _) => Changed();
		Validate();
	}

	private Field? SelectedField => fieldBox.SelectedItem as Field;

	public override RuleSettings ToSettings()
	{
		string key = SelectedField?.Key ?? unknownKey ?? "";
		Operator op = operatorBox.SelectedItem is Operator selected ? selected : Operator.Is;
		return new RuleSettings.Condition(key, op, ValueText());
	}

	private void OnFieldChanged()
	{
		if (loading > 0)
		{
			return;
		}
		loading++;
		bool wasChoice = valueChoice.Visible;
		Operator? previous = operatorBox.SelectedItem as Operator?;
		FillOperators(previous);
		bool isChoice = IsChoiceField();
		if (isChoice != wasChoice || isChoice)
		{
			SetValue(isChoice ? "Yes" : "");
		}
		loading--;
		Changed();
	}

	private void OnOperatorChanged()
	{
		if (loading > 0)
		{
			return;
		}
		loading++;
		ShowValueEditor();
		loading--;
		Changed();
	}

	private void Changed()
	{
		if (loading > 0)
		{
			return;
		}
		Validate();
		RaiseEdited();
	}

	private bool IsChoiceField() => SelectedField?.Kind is FieldKind.YesNo or FieldKind.MangaYesNo;

	private void FillOperators(Operator? prefer)
	{
		Operator[] operators = SelectedField is { } field ? [.. Operators.For(field.Kind)] : [];
		operatorBox.Items.Clear();
		foreach (Operator op in operators)
		{
			operatorBox.Items.Add(op);
		}
		if (prefer is { } wanted && operators.Contains(wanted))
		{
			operatorBox.SelectedItem = wanted;
		}
		else if (operators.Length > 0)
		{
			operatorBox.SelectedIndex = 0;
		}
		ShowValueEditor();
	}

	private void ShowValueEditor()
	{
		bool takesValue = operatorBox.SelectedItem is Operator op && op is not (Operator.IsEmpty or Operator.IsNotEmpty);
		bool choice = IsChoiceField();
		valueText.Visible = takesValue && !choice;
		valueChoice.Visible = takesValue && choice;
		if (choice)
		{
			valueChoice.Items.Clear();
			valueChoice.Items.AddRange(YesNoWords);
			if (SelectedField == Fields.Manga)
			{
				valueChoice.Items.Add(YesRightToLeftWord);
			}
		}
	}

	private void SetValue(string raw)
	{
		ShowValueEditor();
		if (!IsChoiceField())
		{
			valueText.Text = raw;
			return;
		}
		rawChoice = raw;
		int index = Array.FindIndex(YesNoStored, stored => stored == Canonical(raw));
		valueChoice.SelectedIndex = index >= 0 && index < valueChoice.Items.Count ? index : -1;
	}

	private string ValueText()
	{
		if (operatorBox.SelectedItem is Operator.IsEmpty or Operator.IsNotEmpty)
		{
			return "";
		}
		if (!IsChoiceField())
		{
			return valueText.Text;
		}
		return valueChoice.SelectedIndex >= 0 ? YesNoStored[valueChoice.SelectedIndex] : rawChoice;
	}

	private static string? Canonical(string raw)
	{
		string key = new([.. raw.Where(c => !char.IsWhiteSpace(c) && c != '(' && c != ')')]);
		return key.Length == 0 || key.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? "Unknown"
			: key.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? "Yes"
			: key.Equals("No", StringComparison.OrdinalIgnoreCase) ? "No"
			: key.Equals("YesAndRightToLeft", StringComparison.OrdinalIgnoreCase) || key.Equals("YesRightToLeft", StringComparison.OrdinalIgnoreCase) ? "YesAndRightToLeft"
			: null;
	}

	private void Validate()
	{
		string? problem = unknownKey is not null && SelectedField is null
			? $"Unknown field '{unknownKey}'. Choose another field."
			: ProfileValidator.ConditionError((RuleSettings.Condition)ToSettings());
		error.Text = problem ?? "";
		error.Visible = problem is not null;
		Color invalid = Color.FromArgb(255, 225, 225);
		valueText.BackColor = problem is not null && valueText.Visible ? invalid : SystemColors.Window;
		fieldBox.BackColor = problem is not null && SelectedField is null ? invalid : SystemColors.Window;
	}
}

internal sealed class GroupNode : RuleNode
{
	private readonly ComboBox combinator = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, Margin = new Padding(0, 1, 6, 1) };
	private readonly IndentPanel children = new();

	public GroupNode(Combinator initial, IEnumerable<RuleSettings> initialChildren, bool isRoot)
	{
		combinator.Items.AddRange(["Any", "All"]);
		combinator.SelectedIndex = initial == Combinator.All ? 1 : 0;
		combinator.SelectedIndexChanged += (_, _) => RaiseEdited();

		if (!isRoot)
		{
			var remove = Ui.Button("Remove group", (_, _) => RaiseRemove());
			remove.Margin = new Padding(0);
			var header = Ui.Row(
				Ui.Label("Match"),
				combinator,
				Ui.Label("of the following"),
				Ui.Button("Add rule", (_, _) => AddCondition()),
				Ui.Button("Add group", (_, _) => AddGroup()),
				remove);
			Controls.Add(header);
		}
		Controls.Add(children);
		foreach (RuleSettings child in initialChildren)
		{
			Attach(RuleNode.From(child));
		}
		if (isRoot)
		{
			children.Padding = new Padding(0);
			children.DrawGuide = false;
		}
	}

	public int Count => children.Controls.Count;

	public Combinator Combinator => combinator.SelectedIndex == 1 ? Combinator.All : Combinator.Any;

	public void SetCombinator(Combinator value) => combinator.SelectedIndex = value == Combinator.All ? 1 : 0;

	public void AddCondition() => AddAndNotify(new RuleSettings.Condition(Fields.Publisher.Key, Operator.Is, ""));

	public void AddGroup() => AddAndNotify(new RuleSettings.Group(Combinator.Any, [new RuleSettings.Condition(Fields.Publisher.Key, Operator.Is, "")]));

	public void Clear()
	{
		foreach (Control child in children.Controls.Cast<Control>().ToList())
		{
			children.Controls.Remove(child);
			child.Dispose();
		}
	}

	public void Fill(IEnumerable<RuleSettings> items)
	{
		Clear();
		foreach (RuleSettings item in items)
		{
			Attach(RuleNode.From(item));
		}
	}

	public override RuleSettings ToSettings() => ToGroup();

	public RuleSettings.Group ToGroup() => new(Combinator, [.. children.Controls.Cast<RuleNode>().Select(node => node.ToSettings())]);

	private void AddAndNotify(RuleSettings settings)
	{
		Attach(RuleNode.From(settings));
		RaiseEdited();
	}

	private void Attach(RuleNode node)
	{
		node.Edited += (_, _) => RaiseEdited();
		node.RemoveRequested += (_, _) =>
		{
			children.Controls.Remove(node);
			node.Dispose();
			RaiseEdited();
		};
		children.Controls.Add(node);
	}

}
