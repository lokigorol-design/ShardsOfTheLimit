using System.Collections.Generic;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Definitions
{
    public enum AbilityTargetType
    {
        Cell,
        Unit,
        Direction
    }

    public enum AbilityTargetTeam
    {
        Any,
        Allies,
        Enemies
    }

    public abstract class PatternDefinition : ScriptableObject
    {
        public abstract IReadOnlyCollection<GridCoordinate> Evaluate(
            TacticalGrid grid,
            GridCoordinate origin);
    }

    public abstract class AbilityEffectDefinition : ScriptableObject
    {
    }
}
