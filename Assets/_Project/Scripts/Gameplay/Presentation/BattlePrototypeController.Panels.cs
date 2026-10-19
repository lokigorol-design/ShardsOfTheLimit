using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>IMGUI helpers: timeline tiles, onboarding, result screen and on-field preview labels.</summary>
    public sealed partial class BattlePrototypeController
    {        private void EnsureInterfaceStyles()
        {
            if (timelineStyle != null) return;
            timelineStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            timelineStyle.normal.textColor = Color.white;
            timelineActiveStyle = new GUIStyle(timelineStyle) { fontStyle = FontStyle.Bold };
            timelineActiveStyle.normal.textColor = new Color(1f, 0.94f, 0.35f);
            onboardingTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 25,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            onboardingBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            onboardingBodyStyle.normal.textColor = ComicUi.Ink;
            onboardingTitleStyle.normal.textColor = ComicUi.Ink;
            resultTitleStyle = new GUIStyle(onboardingTitleStyle) { fontSize = 30 };
        }

        private void DrawInitiativeTimeline(float x, float y, float width, CombatUnit active)
        {
            if (turns == null) return;
            IReadOnlyList<TimelineEvent> upcoming = turns.GetUpcomingEvents(10, true, showStatusTimelineEvents);
            if (upcoming.Count == 0) return;
            float gap = 4f;
            float tileWidth = Mathf.Min(70f, (width - gap * (upcoming.Count - 1)) / upcoming.Count);
            float currentX = x;
            Color previousBackground = GUI.backgroundColor;
            showStatusTimelineEvents = GUI.Toggle(
                new Rect(x + width - 188f, y - 21f, 188f, 20f),
                showStatusTimelineEvents,
                "Show status events");
            foreach (TimelineEvent timelineEvent in upcoming)
            {
                CombatUnit unit = timelineEvent.Unit;
                GUI.backgroundColor = unit.Team == Team.Player
                    ? new Color(0.22f, 0.5f, 0.82f)
                    : new Color(0.82f, 0.3f, 0.2f);
                string intent = nextEnemyIntent != null && nextEnemyIntent.Unit == unit
                    ? $" {nextEnemyIntent.Codes}"
                    : string.Empty;
                bool isCurrent = timelineEvent.Kind == TimelineEventKind.Activation && unit == active &&
                                 timelineEvent.Tick == turns.CurrentTick;
                string prefix = isCurrent ? "▶" : timelineEvent.Kind == TimelineEventKind.StatusPulse ? "🔥" : string.Empty;
                var tile = new Rect(currentX, y, tileWidth, 40f);
                GUI.Box(tile, $"{prefix} {GetTimelineLabel(unit)}{intent}",
                    isCurrent ? timelineActiveStyle : timelineStyle);
                if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null)
                {
                    GUI.DrawTexture(
                        new Rect(tile.x + 3f, tile.y + 3f, 34f, 34f),
                        definition.Portrait.texture,
                        ScaleMode.ScaleAndCrop,
                        true);
                }
                if (timelineEvent.Kind == TimelineEventKind.StatusPulse)
                {
                    string caption = timelineEvent.StatusId ?? "STATUS";
                    GUI.Box(new Rect(tile.x, tile.y + 24f, tile.width, 16f),
                        new GUIContent(caption.Length > 7 ? caption.Substring(0, 7) : caption, caption));
                }
                currentX += tileWidth + gap;
            }

            GUI.backgroundColor = previousBackground;
            GUI.Label(new Rect(x, y + 41f, width, 20f),
                "Next enemy only: A attack · M movement · H help · S special · – unused action");
        }

        private string GetTimelineLabel(CombatUnit unit)
        {
            if (definitionsByUnit.TryGetValue(unit, out UnitDefinition definition) && definition.Portrait != null)
                return string.Empty;
            string compact = new string(unit.DisplayName.Where(char.IsLetterOrDigit).ToArray());
            return compact.Length <= 4 ? compact.ToUpperInvariant() : compact.Substring(0, 4).ToUpperInvariant();
        }

        private string GetContextHint()
        {
            if (battleEnded) return "Бой окончен. Можно начать заново.";
            if (isBusy) return "Действие выполняется…";
            if (turns?.CurrentUnit?.ControlType != ControlType.Player) return "Ход противника.";
            if (mode == InteractionMode.Facing)
                return "Куда смотрит резонант: наведи на сторону, клик или Enter — подтвердить, ПКМ или Esc — назад.";
            if (mode == InteractionMode.AbilityTargeting && selectedAbilityTarget.HasValue)
                return "Оранжевое — зона удара. Клик по цели применяет способность, правая кнопка — отмена.";
            if (mode == InteractionMode.AbilityTargeting)
                return "Наведи на ярко-зелёную клетку, чтобы увидеть результат. Клик — применить, ПКМ — отмена.";
            return "Наведи на синюю клетку — увидишь путь, клик — идти. 1–5 или иконка — способность. Space — конец хода.";
        }

        private void DrawOnboarding()
        {
            float width = Mathf.Min(660f, Screen.width - 80f);
            float height = 330f;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            ComicUi.Panel(panel, ComicUi.Paper, true, 7f);
            string title;
            string body;
            switch (onboardingPage)
            {
                case 0:
                    title = "1/3 · ПЕРЕМЕЩЕНИЕ";
                    body = "Синие клетки — куда можно дойти, число — цена пути. Наведи курсор на клетку, " +
                           "чтобы увидеть путь, и кликни, чтобы пойти. Подъём на 1–2 уровня стоит дороже, на 3 — нельзя.\n\n" +
                           "Огненный червь ходит под землёй: за 2 ОД выныривает в любой свободной клетке в радиусе 6.\n\n" +
                           "Камера: колесо — зум, правая кнопка с протяжкой — поворот, средняя или WASD — сдвиг, Q/E — поворот на 90°.";
                    break;
                case 1:
                    title = "2/3 · СПОСОБНОСТИ";
                    body = "Кликни по иконке внизу или нажми 1–5. Тёмно-зелёное — вся дальность, ярко-зелёное — " +
                           "допустимые цели. Наведи на цель — увидишь зону удара и числа урона, кликни — способность сработает. " +
                           "Правая кнопка мыши или Esc отменяют выбор.\n\n" +
                           "Огонь по своим включён: области и удары по линии задевают союзников. Наведи на иконку, " +
                           "чтобы прочитать описание.";
                    break;
                default:
                    title = "3/3 · ХОДЫ";
                    body = "Сверху — очередь ходов по Скорости. У следующего противника видно намерение: " +
                           "A — атака, M — движение, H — помощь, S — особое.\n\n" +
                           "У каждого 2 ОД за ход. Над юнитами — здоровье, щит и иконки статусов; наведи на юнита, " +
                           "чтобы увидеть карточку. Space — конец хода, R — начать заново. Победи всех противников.";
                    break;
            }

            ComicUi.Caption(new Rect(panel.x + 24f, panel.y - 18f, Mathf.Min(panel.width - 48f, 60f + title.Length * 13f), 38f),
                title, ComicUi.Yellow, -2.5f, 20);
            GUI.Label(new Rect(panel.x + 34f, panel.y + 44f, panel.width - 68f, 210f), body, onboardingBodyStyle);
            if (ComicUi.Button(new Rect(panel.x + 34f, panel.yMax - 58f, 150f, 36f), "Пропустить (Esc)", hudButtonStyle, true))
                CloseOnboarding();
            string next = onboardingPage < 2 ? "Далее (Enter)" : "В БОЙ!";
            if (ComicUi.Button(new Rect(panel.xMax - 184f, panel.yMax - 58f, 150f, 36f), next, hudButtonStyle, true, false, ComicUi.Yellow))
                AdvanceOnboarding();
        }
        private void AdvanceOnboarding()
        {
            if (onboardingPage < 2)
            {
                onboardingPage++;
                return;
            }

            CloseOnboarding();
        }

        private void CloseOnboarding()
        {
            if (!showOnboarding) return;
            showOnboarding = false;
            feedback?.AddLog("Onboarding complete. Objective: defeat every opponent.");
        }

        private void DrawBattleResult()
        {
            float width = 440f;
            float height = 250f;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f + 40f, width, height);
            ComicUi.Panel(panel, ComicUi.Paper, true, 7f);
            bool victory = outcome == BattleOutcome.PlayerVictory;
            var burst = new Rect(panel.center.x - 190f, panel.y - 120f, 380f, 170f);
            ComicUi.Burst(burst, victory ? ComicUi.Yellow : ComicUi.Red);
            ComicUi.OutlinedText(new Rect(burst.x, burst.y + 40f, burst.width, 60f), victory ? "ПОБЕДА!" : "ПОРАЖЕНИЕ!",
                resultTitleStyle, victory ? Color.white : ComicUi.Yellow, null, 3f);
            GUI.Label(new Rect(panel.x + 36f, panel.y + 70f, panel.width - 72f, 80f),
                $"Тик {turns.CurrentTick} · действий: {resolvedActions}\n" +
                $"Бой длился {Time.unscaledTime - battleStartedAt:0} с · средний ход игрока {AveragePlayerTurnSeconds:0.0} с" +
                (LastReportPath != null ? $"\nОтчёт: Playtests/{System.IO.Path.GetFileName(LastReportPath)}" : string.Empty),
                onboardingBodyStyle);
            if (ComicUi.Button(new Rect(panel.x + 52f, panel.yMax - 64f, 144f, 40f), "ЕЩЁ РАЗ!", hudButtonStyle, true, false, ComicUi.Yellow))
                Restart();
            if (ComicUi.Button(new Rect(panel.xMax - 196f, panel.yMax - 64f, 144f, 40f), "Выход", hudButtonStyle, true)) ExitPrototype();
        }

        private static void ExitPrototype()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void DrawMovementCostLabels()
        {
            if (mode != InteractionMode.Movement || selectedUnit == null ||
                selectedUnit.ControlType != ControlType.Player || battleCamera == null)
            {
                return;
            }

            if (movementCostStyle == null)
            {
                movementCostStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                movementCostStyle.normal.textColor = Color.white;
            }

            foreach (KeyValuePair<GridCoordinate, int> entry in reachable)
            {
                Vector3 screen = battleCamera.WorldToScreenPoint(
                    EffectWorldPosition(entry.Key) + Vector3.up * 0.42f);
                if (screen.z <= 0f) continue;
                var tag = new Rect(screen.x - 13f, Screen.height - screen.y - 12f, 26f, 24f);
                ComicUi.Panel(tag, ComicUi.Paper, false, 2f);
                movementCostStyle.normal.textColor = ComicUi.Ink;
                GUI.Label(tag, entry.Value.ToString(), movementCostStyle);
            }
        }

        private void DrawAbilityPreviewLabels()
        {
            if (mode != InteractionMode.AbilityTargeting || abilityPreview == null || battleCamera == null)
            {
                return;
            }

            if (abilityPreviewStyle == null)
            {
                abilityPreviewStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                abilityPreviewStyle.normal.textColor = Color.white;
            }

            foreach (IGrouping<CombatUnit, ICombatEffectResult> group in abilityPreview.Results.GroupBy(result => result.Target))
            {
                if (!unitViews.TryGetValue(group.Key, out CombatUnitView view)) continue;
                Vector3 screen = battleCamera.WorldToScreenPoint(view.transform.position + Vector3.up * 1.18f);
                if (screen.z <= 0f) continue;
                string label = string.Join("  ", group.Select(BuildPreviewLabel).Where(text => text.Length > 0));
                if (label.Length == 0) label = "NO CHANGE";
                if (selectedUnit != null && group.Key != selectedUnit && group.Any(result => result.Kind == CombatEffectKind.Damage))
                {
                    AttackSide side = FacingRules.GetSide(group.Key, selectedUnit.Cell);
                    if (side != AttackSide.Front) label += side == AttackSide.Back ? "  В СПИНУ!" : "  В БОК!";
                }
                abilityPreviewStyle.normal.textColor = ComicUi.Ink;
                float labelWidth = Mathf.Min(320f, abilityPreviewStyle.CalcSize(new GUIContent(label)).x + 20f);
                var bubble = new Rect(screen.x - labelWidth * 0.5f, Screen.height - screen.y - 16f, labelWidth, 28f);
                ComicUi.Panel(bubble, group.Key.Team == selectedUnit?.Team ? new Color(1f, 0.8f, 0.7f, 0.97f) : ComicUi.Paper, false, 3f);
                GUI.Label(bubble, label, abilityPreviewStyle);
            }

            foreach (TerrainChangeResult terrainResult in abilityPreview.TerrainResults)
            {
                Vector3 screen = battleCamera.WorldToScreenPoint(
                    EffectWorldPosition(terrainResult.Coordinate) + Vector3.up * 0.42f);
                if (screen.z <= 0f) continue;
                var heightTag = new Rect(screen.x - 50f, Screen.height - screen.y - 14f, 100f, 28f);
                ComicUi.Panel(heightTag, ComicUi.Yellow, false, 3f);
                abilityPreviewStyle.normal.textColor = ComicUi.Ink;
                GUI.Label(heightTag, $"H {terrainResult.PreviousHeight}→{terrainResult.NewHeight}", abilityPreviewStyle);
            }
        }

        private static string BuildPreviewLabel(ICombatEffectResult result)
        {
            if (result is StatusAppliedResult status) return $"{status.State.Rules.Id} ×{status.State.Stacks}";
            switch (result.Kind)
            {
                case CombatEffectKind.Damage:
                    string shieldDamage = result.ShieldDelta < 0 ? $"{result.ShieldDelta} SH" : string.Empty;
                    string healthDamage = result.HealthDelta < 0 ? $"{result.HealthDelta} HP" : string.Empty;
                    return $"{shieldDamage} {healthDamage}".Trim();
                case CombatEffectKind.Heal:
                    return result.HealthDelta > 0 ? $"+{result.HealthDelta} HP" : string.Empty;
                case CombatEffectKind.Shield:
                    return result.ShieldDelta > 0 ? $"+{result.ShieldDelta} SH" : string.Empty;
                case CombatEffectKind.Push:
                    var push = (PushResult)result;
                    return push.StepsMoved > 0 ? $"PUSH {push.StepsMoved}" : "BLOCKED";
                case CombatEffectKind.Burning:
                    var burn = (BurnAppliedResult)result;
                    return $"BURN +{burn.AddedDamagePerPulse}/100t → {burn.NewDamagePerPulse}/100t ×{GetBurnPulseCount(burn)}";
                default:
                    return string.Empty;
            }
        }

        private static int GetBurnPulseCount(BurnAppliedResult burn)
        {
            return burn.NewNextPulseTick <= burn.NewExpireTick
                ? (int)((burn.NewExpireTick - burn.NewNextPulseTick) /
                        BurningCalculator.DefaultPulseInterval) + 1
                : 0;
        }
    }
}
