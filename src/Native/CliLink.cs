namespace SourceGit.Native
{
    /// <summary>
    /// Snapshot of the CLI link status on the current system.
    /// </summary>
    public class CliLinkStatus
    {
        /// <summary>
        /// <c>true</c> when a usable <c>sourcegit</c> command is available on PATH.
        /// </summary>
        public bool Installed { get; set; }

        /// <summary>
        /// Human readable path of the installed link/wrapper, or <c>null</c> when not installed.
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// Optional error context gathered while probing (mainly for diagnostics).
        /// </summary>
        public string Error { get; set; }
    }
}
