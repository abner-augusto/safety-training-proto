namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Defines a contract for managers that must reset their state
    /// when a new training session starts.
    /// </summary>
    public interface ISessionResettable
    {
        /// <summary>
        /// Completely resets the internal state of the manager,
        /// such as time, score, task progress, etc.
        /// </summary>
        void ResetSession();
    }
}
