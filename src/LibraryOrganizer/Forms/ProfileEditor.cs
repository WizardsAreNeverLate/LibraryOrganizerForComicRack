using System;
using System.Windows.Forms;

namespace LibraryOrganizer.Plugin.Forms;

/// <summary>
/// One tab of the profile editor. <see cref="Display"/> loads a profile into the controls; <see cref="Collect"/> returns the
/// profile with this tab's settings applied. The tab never keeps a profile of its own.
/// </summary>
internal abstract class ProfileEditor : UserControl
{
	private int loading;

	/// <summary>Raised when the user changes something. Not raised while <see cref="Display"/> fills the controls.</summary>
	public event EventHandler? Edited;

	protected bool IsLoading => loading > 0;

	public void Display(ProfileSettings profile)
	{
		loading++;
		try
		{
			LoadProfile(profile);
		}
		finally
		{
			loading--;
		}
	}

	public abstract ProfileSettings Collect(ProfileSettings profile);

	protected abstract void LoadProfile(ProfileSettings profile);

	protected void RaiseEdited()
	{
		if (!IsLoading)
		{
			Edited?.Invoke(this, EventArgs.Empty);
		}
	}
}
