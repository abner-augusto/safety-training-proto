namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Implemented by items whose "attached to the body" state must reach the pose stream.
    /// Lets PoseReporter (Core) read live attachment state from Runtime components without
    /// referencing the Runtime assembly, and without guessing from transform parenting —
    /// PPE is never re-parented on equip, it only follows its slot's transform.
    /// </summary>
    public interface IPoseAttachment
    {
        /// <summary>
        /// Identifier of what the item is currently attached to, or an empty string when loose.
        /// </summary>
        string AttachmentId { get; }
    }
}
