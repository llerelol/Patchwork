using System.IO;

namespace Patchwork.Attributes {
	/// <summary>
	/// Represents information about a patch and the manner in which patching should be performed. An inheriting class must have a default constructor and be decorated with <see cref="PatchInfoAttribute"/>.
	/// </summary>
	public interface IPatchInfo {
		/// <summary>
		/// Returns the file this patch is meant to patch, when supplied information about the app. This method is supposed to locate the file, etc.
		/// </summary>
		/// <param name="app"></param>
		/// <returns></returns>
		FileInfo GetTargetFile(AppInfo app);

		/// <summary>
		/// Returns the version of the patch.
		/// </summary>
		string PatchVersion { get; }

		/// <summary>
		/// Returns a display of the requirements of the patch.
		/// </summary>
		string Requirements { get; }

		/// <summary>
		/// Returns the display name of the patch.
		/// </summary>
		string PatchName { get; }
	}
}