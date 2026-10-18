using System.Linq;
using NUnit.Framework;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Presentation;
using UnityEngine;

namespace ShardsOfTheLimit.Tests
{
    /// <summary>Ability icons v3: grayscale masters colored by the owner's palette.</summary>
    public sealed class AbilityIconTests
    {
        [TearDown]
        public void TearDown() => IconTintCache.Clear();

        [TestCase("BlazeWorm", "fire")]
        [TestCase("MistPanther", "mist")]
        [TestCase("PrimalIdol", "earth")]
        public void ResonantAbilitiesUseMastersAndTheUnitHasItsPalette(string unitAsset, string palette)
        {
            var unit = Resources.Load<UnitDefinition>("Prototype/Units/" + unitAsset);
            Assert.That(unit.Palette, Is.Not.Null);
            Assert.That(unit.Palette.paletteName, Is.EqualTo(palette));
            foreach (AbilityDefinition ability in unit.Abilities)
                Assert.That(IconTintCache.IsMaster(ability.Icon), Is.True, $"{ability.StableId} uses an Abilities_v3 master.");
        }

        [Test]
        public void TintCacheRendersA256IconAndPaletteChangesTheColor()
        {
            AbilityDefinition swipe = Resources.Load<AbilityDefinition>("Prototype/Abilities/WideSwipe");
            var mist = Resources.Load<ResonantPalette>("Prototype/Palettes/mist");
            var fire = Resources.Load<ResonantPalette>("Prototype/Palettes/fire");
            Texture mistIcon = IconTintCache.Get(swipe.Icon, mist);
            Texture fireIcon = IconTintCache.Get(swipe.Icon, fire);
            Assert.That(mistIcon, Is.Not.Null);
            Assert.That(mistIcon.width, Is.EqualTo(256));
            Assert.That(mistIcon.height, Is.EqualTo(256));
            Assert.That(IconTintCache.Get(swipe.Icon, mist), Is.SameAs(mistIcon), "Cached per master and palette.");
            Color mistCenter = ReadCenter(mistIcon);
            Color fireCenter = ReadCenter(fireIcon);
            Assert.That(Mathf.Abs(mistCenter.r - fireCenter.r) + Mathf.Abs(mistCenter.g - fireCenter.g) +
                        Mathf.Abs(mistCenter.b - fireCenter.b), Is.GreaterThan(0.05f), "The same master is colored differently.");
        }

        [Test]
        public void OldColoredIconsAreNotMasters()
        {
            var mud = Resources.Load<AbilityDefinition>("Prototype/Abilities/MudSpit");
            Assert.That(IconTintCache.IsMaster(mud.Icon), Is.False);
            Assert.That(IconTintCache.Get(mud.Icon, null), Is.Null);
        }

        private static Color ReadCenter(Texture texture)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = (RenderTexture)texture;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            Color sum = Color.clear;
            int count = 0;
            for (int y = 64; y < 192; y += 4)
            for (int x = 64; x < 192; x += 4)
            {
                sum += copy.GetPixel(x, y);
                count++;
            }

            Object.DestroyImmediate(copy);
            return sum / count;
        }
    }
}
