namespace SafetyProto.Runtime.Feedback
{
    /// <summary>
    /// A component that can hold its object in a placed pose away from home: a PPE item worn on a
    /// body slot, a scaffold piece installed in its socket. <see cref="ReturnObjectHome"/> reads it
    /// every frame and leaves a secured object where it is.
    /// </summary>
    public interface ISecuredPlacement
    {
        bool IsSecured { get; }
    }
}
