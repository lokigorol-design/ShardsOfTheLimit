using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>Builds distinct temporary low-poly silhouettes without external art dependencies.</summary>
    public static class PlaceholderModelFactory
    {
        public static GameObject Create(UnitDefinition definition, Team team, Color primary)
        {
            var root = new GameObject($"{definition.DisplayName} Low Poly Model");
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, -0.02f, 0f);
            collider.height = 1.25f;
            collider.radius = 0.34f;

            Color accent = team == Team.Player
                ? Color.Lerp(primary, Color.white, 0.48f)
                : Color.Lerp(primary, new Color(0.2f, 0.04f, 0.02f), 0.38f);
            Color dark = Color.Lerp(primary, Color.black, 0.48f);
            AddPart(root.transform, "Team Base", PrimitiveType.Cylinder,
                new Vector3(0f, -0.58f, 0f), new Vector3(0.43f, 0.035f, 0.43f), primary);

            switch (definition.StableId)
            {
                case "blaze-worm":
                    BuildBlazeWorm(root.transform, primary, accent, dark);
                    break;
                case "azure-vanguard":
                    BuildVanguard(root.transform, primary, accent, dark);
                    break;
                case "cobalt-ranger":
                    BuildRanger(root.transform, primary, accent, dark);
                    break;
                case "teal-skirmisher":
                    BuildSkirmisher(root.transform, primary, accent, dark);
                    break;
                case "crimson-brute":
                    BuildBrute(root.transform, primary, accent, dark);
                    break;
                case "archon":
                    BuildArchon(root.transform, primary, accent, dark);
                    break;
                default:
                    BuildRaider(root.transform, primary, accent, dark);
                    break;
            }

            return root;
        }

        private static void BuildBlazeWorm(Transform root, Color primary, Color accent, Color dark)
        {
            AddPart(root, "Tail", PrimitiveType.Sphere, new Vector3(0f, -0.28f, 0.34f),
                new Vector3(0.35f, 0.24f, 0.45f), dark);
            AddPart(root, "Rear Segment", PrimitiveType.Sphere, new Vector3(0f, -0.18f, 0.12f),
                new Vector3(0.46f, 0.33f, 0.5f), primary);
            AddPart(root, "Core Segment", PrimitiveType.Sphere, new Vector3(0f, -0.02f, -0.08f),
                new Vector3(0.56f, 0.42f, 0.52f), primary);
            AddPart(root, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.23f, -0.28f),
                new Vector3(0.64f, 0.48f, 0.54f), primary);
            AddPart(root, "Mouth", PrimitiveType.Cube, new Vector3(0f, 0.17f, -0.59f),
                new Vector3(0.38f, 0.11f, 0.2f), dark);
            AddPart(root, "Left Fang", PrimitiveType.Cube, new Vector3(-0.18f, 0.12f, -0.67f),
                new Vector3(0.07f, 0.2f, 0.07f), accent, new Vector3(-28f, 0f, -12f));
            AddPart(root, "Right Fang", PrimitiveType.Cube, new Vector3(0.18f, 0.12f, -0.67f),
                new Vector3(0.07f, 0.2f, 0.07f), accent, new Vector3(-28f, 0f, 12f));
            AddPart(root, "Left Eye", PrimitiveType.Sphere, new Vector3(-0.21f, 0.36f, -0.5f),
                Vector3.one * 0.09f, accent);
            AddPart(root, "Right Eye", PrimitiveType.Sphere, new Vector3(0.21f, 0.36f, -0.5f),
                Vector3.one * 0.09f, accent);
            AddPart(root, "Flame Crest", PrimitiveType.Cube, new Vector3(0f, 0.58f, -0.18f),
                new Vector3(0.12f, 0.34f, 0.18f), accent, new Vector3(20f, 0f, 0f));
        }

        private static void BuildVanguard(Transform root, Color primary, Color accent, Color dark)
        {
            AddPart(root, "Armoured Torso", PrimitiveType.Cube, new Vector3(0f, 0f, 0f),
                new Vector3(0.48f, 0.56f, 0.34f), primary);
            AddPart(root, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.43f, 0f),
                Vector3.one * 0.27f, accent);
            AddPart(root, "Left Shoulder", PrimitiveType.Sphere, new Vector3(-0.34f, 0.14f, 0f),
                new Vector3(0.25f, 0.2f, 0.28f), accent);
            AddPart(root, "Right Shoulder", PrimitiveType.Sphere, new Vector3(0.34f, 0.14f, 0f),
                new Vector3(0.25f, 0.2f, 0.28f), accent);
            AddPart(root, "Shield", PrimitiveType.Cylinder, new Vector3(-0.42f, -0.08f, -0.04f),
                new Vector3(0.33f, 0.08f, 0.33f), accent, new Vector3(0f, 0f, 90f));
            AddLegs(root, dark);
        }

        private static void BuildRanger(Transform root, Color primary, Color accent, Color dark)
        {
            AddPart(root, "Ranger Torso", PrimitiveType.Capsule, new Vector3(0f, -0.02f, 0f),
                new Vector3(0.34f, 0.43f, 0.3f), primary);
            AddPart(root, "Visor", PrimitiveType.Sphere, new Vector3(0f, 0.43f, -0.03f),
                new Vector3(0.24f, 0.2f, 0.24f), accent);
            AddPart(root, "Long Rifle", PrimitiveType.Cylinder, new Vector3(0.34f, 0.04f, 0f),
                new Vector3(0.055f, 0.48f, 0.055f), dark, new Vector3(18f, 0f, -12f));
            AddPart(root, "Emitter", PrimitiveType.Sphere, new Vector3(0.23f, 0.48f, 0.05f),
                Vector3.one * 0.1f, accent);
            AddLegs(root, dark);
        }

        private static void BuildSkirmisher(Transform root, Color primary, Color accent, Color dark)
        {
            AddPart(root, "Skirmisher Torso", PrimitiveType.Capsule, new Vector3(0f, -0.03f, 0f),
                new Vector3(0.32f, 0.4f, 0.28f), primary);
            AddPart(root, "Crest", PrimitiveType.Sphere, new Vector3(0f, 0.43f, 0f),
                new Vector3(0.22f, 0.25f, 0.22f), accent);
            AddPart(root, "Left Blade", PrimitiveType.Cube, new Vector3(-0.35f, -0.02f, 0f),
                new Vector3(0.07f, 0.48f, 0.12f), accent, new Vector3(0f, 0f, -28f));
            AddPart(root, "Right Blade", PrimitiveType.Cube, new Vector3(0.35f, -0.02f, 0f),
                new Vector3(0.07f, 0.48f, 0.12f), accent, new Vector3(0f, 0f, 28f));
            AddLegs(root, dark);
        }

        private static void BuildBrute(Transform root, Color primary, Color accent, Color dark)
        {
            AddPart(root, "Brute Torso", PrimitiveType.Cube, new Vector3(0f, -0.02f, 0f),
                new Vector3(0.66f, 0.58f, 0.5f), primary);
            AddPart(root, "Brute Head", PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f),
                new Vector3(0.4f, 0.27f, 0.36f), accent);
            AddPart(root, "Left Fist", PrimitiveType.Sphere, new Vector3(-0.45f, -0.02f, 0f),
                Vector3.one * 0.28f, dark);
            AddPart(root, "Right Fist", PrimitiveType.Sphere, new Vector3(0.45f, -0.02f, 0f),
                Vector3.one * 0.28f, dark);
            AddLegs(root, dark, 0.21f);
        }

        /// <summary>The Archon: a hooded figure in a long coat with a staff holding a glowing resonance stone.</summary>
        private static void BuildArchon(Transform root, Color primary, Color accent, Color dark)
        {
            Color coat = new Color(0.16f, 0.18f, 0.26f);
            AddPart(root, "Coat", PrimitiveType.Cylinder, new Vector3(0f, -0.22f, 0f),
                new Vector3(0.42f, 0.36f, 0.42f), coat);
            AddPart(root, "Torso", PrimitiveType.Capsule, new Vector3(0f, 0.12f, 0f),
                new Vector3(0.34f, 0.32f, 0.3f), coat);
            AddPart(root, "Mantle", PrimitiveType.Sphere, new Vector3(0f, 0.3f, 0f),
                new Vector3(0.46f, 0.16f, 0.4f), new Color(0.55f, 0.42f, 0.2f));
            AddPart(root, "Hood", PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0.02f),
                new Vector3(0.26f, 0.28f, 0.27f), Color.Lerp(coat, Color.black, 0.35f));
            AddPart(root, "Face", PrimitiveType.Sphere, new Vector3(0f, 0.48f, -0.08f),
                new Vector3(0.15f, 0.15f, 0.1f), new Color(0.85f, 0.7f, 0.58f));
            AddPart(root, "Staff", PrimitiveType.Cylinder, new Vector3(0.3f, 0.05f, -0.06f),
                new Vector3(0.04f, 0.6f, 0.04f), new Color(0.35f, 0.24f, 0.14f));
            AddPart(root, "Resonance Stone", PrimitiveType.Sphere, new Vector3(0.3f, 0.7f, -0.06f),
                Vector3.one * 0.15f, accent);
        }

        private static void BuildRaider(Transform root, Color primary, Color accent, Color dark)
        {
            AddPart(root, "Raider Torso", PrimitiveType.Capsule, new Vector3(0f, -0.03f, 0f),
                new Vector3(0.38f, 0.43f, 0.34f), primary);
            AddPart(root, "Raider Head", PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f),
                new Vector3(0.3f, 0.24f, 0.28f), accent);
            AddPart(root, "Spear", PrimitiveType.Cylinder, new Vector3(0.38f, 0.02f, 0f),
                new Vector3(0.045f, 0.52f, 0.045f), dark, new Vector3(0f, 0f, -12f));
            AddPart(root, "Spear Tip", PrimitiveType.Sphere, new Vector3(0.49f, 0.51f, 0f),
                new Vector3(0.09f, 0.15f, 0.09f), accent);
            AddLegs(root, dark);
        }

        private static void AddLegs(Transform root, Color color, float width = 0.15f)
        {
            AddPart(root, "Left Leg", PrimitiveType.Cube, new Vector3(-0.16f, -0.42f, 0f),
                new Vector3(width, 0.34f, 0.18f), color);
            AddPart(root, "Right Leg", PrimitiveType.Cube, new Vector3(0.16f, -0.42f, 0f),
                new Vector3(width, 0.34f, 0.18f), color);
        }

        private static GameObject AddPart(
            Transform parent,
            string name,
            PrimitiveType primitive,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            Vector3? localEulerAngles = null)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localEulerAngles = localEulerAngles ?? Vector3.zero;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            part.GetComponent<Renderer>().material.color = color;
            return part;
        }
    }
}
