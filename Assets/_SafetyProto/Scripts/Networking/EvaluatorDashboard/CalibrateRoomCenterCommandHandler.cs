namespace SafetyProto.Runtime
{
    /// <summary>
    /// Evaluator dashboard command handler for "calibrate_room_center". The evaluator stands the
    /// participant on the floor mark facing the room's front and sends the command: the head
    /// pose is recorded as the room center (overriding the automatic session-start capture),
    /// then the participant is recentered onto the current phase's anchor from it, exactly like
    /// "recenter_player".
    /// </summary>
    public class CalibrateRoomCenterCommandHandler : RecenterCommandHandler
    {
        private const string CalibrateCommandName = "calibrate_room_center";

        public override string Command => CalibrateCommandName;

        protected override void BeforeRecenter(RecenterService service)
        {
            service.CaptureRoomCenter(RoomCenterSource.Operator);
        }
    }
}
