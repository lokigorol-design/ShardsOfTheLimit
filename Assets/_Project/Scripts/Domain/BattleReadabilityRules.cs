using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    public enum BattleOutcome
    {
        Ongoing,
        PlayerVictory,
        PlayerDefeat
    }

    public enum BattleIntentKind
    {
        None,
        Attack,
        Movement,
        Help,
        Special
    }

    public static class BattleReadabilityRules
    {
        public static BattleOutcome EvaluateOutcome(IEnumerable<CombatUnit> units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            CombatUnit[] battleUnits = units.Where(unit => unit != null).ToArray();
            bool playerAlive = battleUnits.Any(unit => unit.Team == Team.Player && !unit.IsDefeated);
            bool opponentAlive = battleUnits.Any(unit => unit.Team == Team.Opponent && !unit.IsDefeated);
            if (playerAlive && opponentAlive) return BattleOutcome.Ongoing;
            return playerAlive ? BattleOutcome.PlayerVictory : BattleOutcome.PlayerDefeat;
        }

        public static string GetIntentCode(BattleIntentKind kind)
        {
            switch (kind)
            {
                case BattleIntentKind.Attack: return "A";
                case BattleIntentKind.Movement: return "M";
                case BattleIntentKind.Help: return "H";
                case BattleIntentKind.Special: return "S";
                default: return "–";
            }
        }
    }
}
