using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Playtest measurements: real time of every activation, battle length, abilities used and reserve actions.
    /// At the end of a battle a plain-text report is written to the Playtests folder next to the game.
    /// </summary>
    public sealed partial class BattlePrototypeController
    {
        private sealed class ActivationRecord
        {
            public string Unit;
            public bool Player;
            public float Seconds;
        }

        private readonly List<ActivationRecord> activationRecords = new List<ActivationRecord>();
        private readonly Dictionary<string, int> abilityUses = new Dictionary<string, int>();
        private CombatUnit measuredUnit;
        private float activationStartedAt;
        private int reserveActions;
        private bool reportWritten;

        /// <summary>Path of the last written playtest report, or null.</summary>
        public string LastReportPath { get; private set; }

        public float AveragePlayerTurnSeconds
        {
            get
            {
                float[] player = activationRecords.Where(r => r.Player).Select(r => r.Seconds).ToArray();
                return player.Length == 0 ? 0f : player.Average();
            }
        }

        private void MeasureActivationStart(CombatUnit unit)
        {
            measuredUnit = unit;
            activationStartedAt = Time.unscaledTime;
        }

        private void MeasureActivationEnd(CombatUnit unit)
        {
            if (unit == null || unit != measuredUnit) return;
            activationRecords.Add(new ActivationRecord
            {
                Unit = unit.DisplayName,
                Player = unit.ControlType == ControlType.Player,
                Seconds = Time.unscaledTime - activationStartedAt
            });
            measuredUnit = null;
        }

        private void MeasureAbility(AbilityDefinition ability)
        {
            abilityUses.TryGetValue(ability.DisplayName, out int count);
            abilityUses[ability.DisplayName] = count + 1;
        }

        private static string PlaytestFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Playtests"));

        private void WritePlaytestReport()
        {
            if (reportWritten) return;
            reportWritten = true;
            MeasureActivationEnd(measuredUnit);
            try
            {
                Directory.CreateDirectory(PlaytestFolder);
                string path = Path.Combine(PlaytestFolder, $"battle_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, BuildPlaytestReport(), Encoding.UTF8);
                LastReportPath = path;
                feedback.AddLog($"Отчёт плейтеста: {path}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Playtest report was not written: {exception.Message}");
            }
        }

        private string BuildPlaytestReport()
        {
            var text = new StringBuilder();
            float battleSeconds = Time.unscaledTime - battleStartedAt;
            ActivationRecord[] player = activationRecords.Where(r => r.Player).ToArray();
            text.AppendLine("Осколки Предела — отчёт плейтеста");
            text.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm}");
            text.AppendLine($"Итог: {(outcome == BattleOutcome.PlayerVictory ? "победа" : outcome == BattleOutcome.PlayerDefeat ? "поражение" : "не завершён")}");
            text.AppendLine($"Длительность боя: {battleSeconds / 60f:0.0} мин ({battleSeconds:0} с), тик {turns?.CurrentTick ?? 0}");
            text.AppendLine($"Ходов игрока: {player.Length}, ходов противника: {activationRecords.Count - player.Length}");
            if (player.Length > 0)
            {
                float[] seconds = player.Select(r => r.Seconds).OrderBy(s => s).ToArray();
                text.AppendLine($"Ход игрока: среднее {seconds.Average():0.0} с, медиана {seconds[seconds.Length / 2]:0.0} с, " +
                                $"максимум {seconds.Last():0.0} с");
            }

            text.AppendLine($"Действий всего: {resolvedActions}, призывов, замен и отзывов: {reserveActions}");
            text.AppendLine();
            text.AppendLine("Время хода по резонантам (игрок):");
            foreach (var group in player.GroupBy(r => r.Unit))
                text.AppendLine($"  {group.Key}: {group.Count()} ход(ов), в среднем {group.Average(r => r.Seconds):0.0} с");
            text.AppendLine();
            text.AppendLine("Способности:");
            foreach (var use in abilityUses.OrderByDescending(pair => pair.Value))
                text.AppendLine($"  {use.Key}: {use.Value}");
            text.AppendLine();
            text.AppendLine("Ветки:");
            foreach (CombatUnit unit in definitionsByUnit.Keys.Where(unit => unit.Team == Team.Player))
            {
                string branch = BranchVariantName(unit);
                if (branch.Length > 0) text.AppendLine($"  {branch}");
            }

            text.AppendLine();
            text.AppendLine("Состояние резонантов в конце:");
            foreach (CombatUnit unit in definitionsByUnit.Keys)
                text.AppendLine($"  [{unit.Team}] {unit.DisplayName}: {unit.CurrentHealth}/{unit.MaximumHealth} HP" +
                                (unit.IsDefeated ? " — пал" : string.Empty) +
                                (reserve.Any(slot => slot.Unit == unit) ? " (в резерве)" : string.Empty));
            return text.ToString();
        }
    }
}
