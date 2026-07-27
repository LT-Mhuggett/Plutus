namespace Plutus.Frontend.ClientUI.Services.IOHandeling
{
    /// <summary>
    /// Minimal file IO abstraction used by Settings for local database backup/restore/delete.
    /// A trimmed port of the NatApp IFile DependencyService, implemented with the cross-platform
    /// FilePicker (Essentials) and CommunityToolkit.Maui FileSaver.
    /// </summary>
    public interface IFileService
    {
        /// <summary>
        /// Prompt the user for a destination and save a copy of the file at <paramref name="sourcePath"/>.
        /// </summary>
        /// <returns>True if the file was saved.</returns>
        Task<bool> SaveCopyAsync(string sourcePath, string suggestedName);

        /// <summary>
        /// Prompt the user to pick a file and return its full path (null if cancelled).
        /// </summary>
        Task<string> PickFileAsync(params string[] allowedExtensions);

        /// <summary>
        /// Delete the file at <paramref name="path"/>. Returns true if it no longer exists afterwards.
        /// </summary>
        bool DeleteFile(string path);
    }
}
