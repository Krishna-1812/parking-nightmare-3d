namespace PN3D.Core
{
    /// <summary>
    /// The run's single state value, mirroring <c>Game.state</c> in the reference
    /// implementation. Several rules gate on it — shame only accrues in
    /// Drive/Park/Settle, style only in Drive, and shame only decays in Drive — so it is
    /// one shared value rather than a per-system flag.
    /// </summary>
    public enum GamePhase
    {
        /// <summary>Driving the route; the spot is not armed yet.</summary>
        Drive,
        /// <summary>Inside the parking zone, not currently within tolerance.</summary>
        Park,
        /// <summary>Within tolerance and holding still; the settle timer is counting.</summary>
        Settle,
        Success,
        Fail,
    }
}
