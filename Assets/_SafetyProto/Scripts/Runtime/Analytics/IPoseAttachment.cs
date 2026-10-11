namespace SafetyProto.Runtime.Analytics
{
    /// <summary>
    /// Implemented by items whose "attached to the body" state must reach the pose stream.
    /// Lets <c>PoseReporter</c> read live attachment state without guessing from transform
    /// parenting — PPE is never re-parented on equip, it only follows its slot's transform.
    /// </summary>
    public interface IPoseAttachment
    {
        /// <summary>
        /// Identifier of what the item is currently attached to, or an empty string when loose.
        /// </summary>
        string AttachmentId { get; }
    }
}
