namespace NzbDrone.Core.Tv
{
    /// <summary>
    /// What to do with existing episode files when a series' root folder is changed.
    /// Sent by the frontend as part of the PUT /api/v5/series/{id} request.
    /// </summary>
    public enum RootFolderAction
    {
        /// <summary>Physically move all files to the new root folder path (legacy behavior).</summary>
        MoveFiles = 0,

        /// <summary>
        /// Create hardlinks in the new location. Original files stay in place.
        /// Requires source and destination to be on the same filesystem.
        /// </summary>
        HardlinkToNew = 1,

        /// <summary>Only update the stored path in the database. No file operations.</summary>
        PathUpdateOnly = 2
    }
}
