using UnityEngine;

namespace FPSGame.Networking
{
    /// <summary>
    /// Custom network message types for game events.
    /// Will use Mirror's MessageBase in Phase 2.
    /// </summary>

    /// <summary>
    /// Message sent when a player fires their weapon.
    /// </summary>
    [System.Serializable]
    public struct WeaponFireMessage
    {
        public uint shooterId;
        public Vector3 origin;
        public Vector3 direction;
        public string weaponName;
        public float timestamp;
    }

    /// <summary>
    /// Message sent when a player takes damage.
    /// </summary>
    [System.Serializable]
    public struct DamageMessage
    {
        public uint targetId;
        public uint attackerId;
        public float damage;
        public Vector3 hitPoint;
        public Vector3 hitNormal;
    }

    /// <summary>
    /// Message sent when a player dies.
    /// </summary>
    [System.Serializable]
    public struct PlayerDeathMessage
    {
        public uint victimId;
        public uint killerId;
        public string weaponUsed;
        public bool wasHeadshot;
    }

    /// <summary>
    /// Message sent when a player respawns.
    /// </summary>
    [System.Serializable]
    public struct PlayerRespawnMessage
    {
        public uint playerId;
        public Vector3 spawnPosition;
        public Quaternion spawnRotation;
    }

    /// <summary>
    /// Message for chat communication.
    /// </summary>
    [System.Serializable]
    public struct ChatMessage
    {
        public uint senderId;
        public string senderName;
        public string message;
        public bool isTeamChat;
    }

    /// <summary>
    /// Message for match state changes.
    /// </summary>
    [System.Serializable]
    public struct MatchStateMessage
    {
        public MatchState state;
        public float timeRemaining;
        public int[] teamScores;
    }

    public enum MatchState
    {
        WaitingForPlayers,
        Countdown,
        InProgress,
        RoundEnd,
        MatchEnd
    }

    /// <summary>
    /// Message for player score updates.
    /// </summary>
    [System.Serializable]
    public struct ScoreUpdateMessage
    {
        public uint playerId;
        public int kills;
        public int deaths;
        public int assists;
        public int score;
    }
}
