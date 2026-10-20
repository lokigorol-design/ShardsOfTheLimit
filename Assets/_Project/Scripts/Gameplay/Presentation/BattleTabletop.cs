using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// The battle as a game laid out on a kitchen table: a printed board under the cardboard cells, a wooden table with
    /// dice, cards, a rulebook and a pencil around it, and a room behind that the depth of field blurs away.
    /// Everything is procedural (primitives + generated textures); no assets or packages are added.
    /// Scale: one cell ≈ 2.5 cm, so the table is ~30 units high and the room walls are far away.
    /// </summary>
    public static class BattleTabletop
    {
        public const float BaseHeight = 0.06f;
        private const int Size = 256;

        private static Texture2D woodTexture;
        private static Texture2D cardboardTexture;
        private static Texture2D cardboardNormal;
        private static Material woodMaterial;
        private static Material baseRimPlayer;
        private static Material baseRimOpponent;

        public static void Build(Transform parent, float boardTopY, float boardHalfExtent)
        {
            var root = new GameObject("Tabletop").transform;
            root.SetParent(parent, false);

            float boardThickness = 0.14f;
            float tableTopY = boardTopY - boardThickness;
            float boardHalf = boardHalfExtent + 0.55f;

            // Printed game board under the cells: dark felt-green with an inked border and a gold frame line.
            GameObject board = Box(root, "Game Board", new Vector3(0f, boardTopY - boardThickness * 0.5f, 0f),
                new Vector3(boardHalf * 2f, boardThickness, boardHalf * 2f), Vector3.zero,
                Lit(BoardTexture(), Color.white, 0.08f));
            board.GetComponent<Renderer>().receiveShadows = true;

            // Table: wide wooden top with legs down to the floor.
            const float tableHalfX = 23f;
            const float tableHalfZ = 15f;
            const float tableThickness = 1.6f;
            float floorY = tableTopY - 30f;
            Material wood = WoodMaterial;
            Material tableTop = Lit(WoodTexture(), new Color(0.6f, 0.44f, 0.32f), 0.38f);
            tableTop.mainTextureScale = new Vector2(7f, 2.5f);
            Box(root, "Table Top", new Vector3(0f, tableTopY - tableThickness * 0.5f, 3f),
                new Vector3(tableHalfX * 2f, tableThickness, tableHalfZ * 2f), Vector3.zero, tableTop);
            for (int index = 0; index < 4; index++)
            {
                float x = (index % 2 == 0 ? -1f : 1f) * (tableHalfX - 2f);
                float z = 3f + (index < 2 ? -1f : 1f) * (tableHalfZ - 2f);
                Box(root, "Table Leg", new Vector3(x, (tableTopY + floorY) * 0.5f, z),
                    new Vector3(2.2f, tableTopY - floorY - tableThickness, 2.2f), Vector3.zero, wood);
            }

            BuildProps(root, tableTopY, boardHalf);
            BuildRoom(root, floorY);
        }

        /// <summary>Things lying on the table around the board.</summary>
        private static void BuildProps(Transform root, float y, float boardHalf)
        {
            Texture2D pips = DieTexture();
            Material die = Lit(pips, new Color(0.96f, 0.94f, 0.9f), 0.45f);
            Box(root, "Die", new Vector3(boardHalf + 3.4f, y + 0.3f, -3.2f), Vector3.one * 0.6f, new Vector3(0f, 23f, 0f), die);
            Box(root, "Die", new Vector3(boardHalf + 4.3f, y + 0.3f, -2.4f), Vector3.one * 0.6f, new Vector3(0f, -38f, 0f),
                Lit(pips, new Color(0.85f, 0.22f, 0.18f), 0.45f));

            Material cardBack = Lit(CardTexture(new Color(0.55f, 0.12f, 0.12f), new Color(0.95f, 0.8f, 0.45f)), Color.white, 0.25f);
            Box(root, "Card Deck", new Vector3(-boardHalf - 3.4f, y + 0.25f, 4f), new Vector3(1.7f, 0.5f, 2.4f),
                new Vector3(0f, -8f, 0f), cardBack);
            Box(root, "Card", new Vector3(-boardHalf - 3f, y + 0.02f, 0.8f), new Vector3(1.7f, 0.04f, 2.4f),
                new Vector3(0f, 21f, 0f), cardBack);

            Material rulebook = Lit(CardTexture(new Color(0.12f, 0.2f, 0.38f), new Color(0.9f, 0.75f, 0.35f)), Color.white, 0.2f);
            Box(root, "Rulebook", new Vector3(-boardHalf - 8f, y + 0.15f, -6f), new Vector3(6f, 0.3f, 8.2f),
                new Vector3(0f, 14f, 0f), rulebook);

            // Pencil lying on the table.
            GameObject pencil = Cylinder(root, "Pencil", new Vector3(boardHalf + 5f, y + 0.15f, 5f), new Vector3(0.3f, 3.2f, 0.3f),
                new Vector3(90f, 0f, 62f), Lit(null, new Color(0.98f, 0.76f, 0.15f), 0.35f));
            Cylinder(pencil.transform, "Eraser", new Vector3(0f, 1.05f, 0f), new Vector3(1f, 0.1f, 1f), Vector3.zero,
                Lit(null, new Color(0.95f, 0.55f, 0.6f), 0.1f), true);

            // Spare wooden tokens and a stack of figure bases.
            for (int index = 0; index < 4; index++)
                Cylinder(root, "Spare Token", new Vector3(boardHalf + 2.4f, y + 0.04f + index * 0.08f, 1.8f),
                    new Vector3(0.9f, 0.04f, 0.9f), new Vector3(0f, index * 30f, 0f), WoodMaterial);
            Cylinder(root, "Spare Token", new Vector3(-boardHalf - 2f, y + 0.04f, -3.4f), new Vector3(0.9f, 0.04f, 0.9f),
                Vector3.zero, WoodMaterial);

            // A mug at the far end of the table; it falls into the blur.
            Cylinder(root, "Mug", new Vector3(-15f, y + 2.1f, 15.5f), new Vector3(3.2f, 2.1f, 3.2f), Vector3.zero,
                Lit(null, new Color(0.25f, 0.45f, 0.55f), 0.6f));
        }

        /// <summary>Kitchen around the table: floor, walls, a bright window, cupboards and a shelf.</summary>
        private static void BuildRoom(Transform root, float floorY)
        {
            const float half = 80f;
            const float height = 110f;
            Material floor = Lit(WoodTexture(), new Color(0.6f, 0.45f, 0.32f), 0.2f);
            floor.mainTextureScale = new Vector2(10f, 10f);
            Box(root, "Floor", new Vector3(0f, floorY - 0.5f, 0f), new Vector3(half * 2f, 1f, half * 2f), Vector3.zero, floor);

            Material wall = Lit(null, new Color(0.86f, 0.78f, 0.62f), 0.05f);
            Material wallAccent = Lit(null, new Color(0.55f, 0.7f, 0.62f), 0.05f);
            Box(root, "Wall N", new Vector3(0f, floorY + height * 0.5f, half), new Vector3(half * 2f, height, 1f), Vector3.zero, wall);
            Box(root, "Wall S", new Vector3(0f, floorY + height * 0.5f, -half), new Vector3(half * 2f, height, 1f), Vector3.zero, wallAccent);
            Box(root, "Wall E", new Vector3(half, floorY + height * 0.5f, 0f), new Vector3(1f, height, half * 2f), Vector3.zero, wall);
            Box(root, "Wall W", new Vector3(-half, floorY + height * 0.5f, 0f), new Vector3(1f, height, half * 2f), Vector3.zero, wallAccent);

            // Window in the north wall: bright glass that blooms, with a wooden frame and cross bars.
            var glass = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { name = "Window Light" };
            glass.SetColor("_BaseColor", new Color(1.7f, 1.6f, 1.35f));
            glass.color = new Color(1.7f, 1.6f, 1.35f);
            Box(root, "Window", new Vector3(10f, floorY + 62f, half - 0.6f), new Vector3(46f, 34f, 0.2f), Vector3.zero, glass)
                .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Material frame = Lit(null, new Color(0.95f, 0.93f, 0.88f), 0.3f);
            Box(root, "Window Bar", new Vector3(10f, floorY + 62f, half - 0.8f), new Vector3(1.6f, 34f, 0.4f), Vector3.zero, frame);
            Box(root, "Window Bar", new Vector3(10f, floorY + 62f, half - 0.8f), new Vector3(46f, 1.6f, 0.4f), Vector3.zero, frame);
            Box(root, "Window Sill", new Vector3(10f, floorY + 44.5f, half - 2f), new Vector3(50f, 1.5f, 4f), Vector3.zero, frame);

            // Kitchen counter and cupboards along the east wall.
            Material cupboard = Lit(null, new Color(0.42f, 0.62f, 0.55f), 0.25f);
            Material counter = Lit(WoodTexture(), new Color(0.75f, 0.58f, 0.4f), 0.3f);
            Box(root, "Counter", new Vector3(half - 10f, floorY + 18f, 10f), new Vector3(20f, 36f, 70f), Vector3.zero, cupboard);
            Box(root, "Counter Top", new Vector3(half - 10f, floorY + 36.8f, 10f), new Vector3(21f, 1.6f, 71f), Vector3.zero, counter);
            Box(root, "Upper Cupboard", new Vector3(half - 7f, floorY + 78f, 10f), new Vector3(14f, 26f, 70f), Vector3.zero, cupboard);

            // Shelf with jars on the west wall.
            Box(root, "Shelf", new Vector3(-half + 5f, floorY + 60f, -5f), new Vector3(10f, 1.5f, 50f), Vector3.zero, counter);
            Color[] jarColors = { new Color(0.9f, 0.55f, 0.25f), new Color(0.85f, 0.25f, 0.2f), new Color(0.95f, 0.85f, 0.4f), new Color(0.4f, 0.55f, 0.75f) };
            for (int index = 0; index < 6; index++)
                Cylinder(root, "Jar", new Vector3(-half + 5f, floorY + 65f, -25f + index * 8f), new Vector3(5f, 4.2f, 5f),
                    Vector3.zero, Lit(null, jarColors[index % jarColors.Length], 0.55f));
        }

        /// <summary>
        /// Round wooden base under a figure: a team-painted rim with a raw wood top, BaseHeight thick. Its top is at the
        /// given local height of the parent.
        /// </summary>
        public static GameObject CreateFigureBase(Transform parent, float localTopY, float radius, bool playerTeam)
        {
            var root = new GameObject("Figure Base");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, localTopY - BaseHeight, 0f);
            Material rim = playerTeam
                ? baseRimPlayer ??= Lit(null, new Color(0.18f, 0.42f, 0.78f), 0.35f)
                : baseRimOpponent ??= Lit(null, new Color(0.72f, 0.18f, 0.14f), 0.35f);
            Cylinder(root.transform, "Rim", new Vector3(0f, BaseHeight * 0.45f, 0f),
                new Vector3(radius * 2f, BaseHeight * 0.45f, radius * 2f), Vector3.zero, rim, true);
            Cylinder(root.transform, "Wood", new Vector3(0f, BaseHeight * 0.5f, 0f),
                new Vector3(radius * 1.78f, BaseHeight * 0.5f, radius * 1.78f), Vector3.zero, WoodMaterial, true);
            return root;
        }

        /// <summary>Plain wooden token, e.g. under obstacle rocks; its top is at the given world height.</summary>
        public static GameObject CreateToken(Transform parent, Vector3 worldTopCenter, float radius)
        {
            GameObject token = Cylinder(parent, "Token", worldTopCenter - Vector3.up * BaseHeight * 0.5f,
                new Vector3(radius * 2f, BaseHeight * 0.5f, radius * 2f), Vector3.zero, WoodMaterial);
            return token;
        }

        /// <summary>Printed cardboard for the cell columns: kraft fibres, a faint printed stone pattern and an inked edge.</summary>
        public static void CardboardTextures(out Texture2D albedo, out Texture2D normal)
        {
            if (cardboardTexture == null)
            {
                var heights = new float[Size * Size];
                var colors = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float u = x / (float)Size;
                        float v = y / (float)Size;
                        float fibres = Mathf.PerlinNoise(u * 60f, v * 7f) * 0.5f + Mathf.PerlinNoise(u * 9f + 30f, v * 70f) * 0.5f;
                        float print = Mathf.PerlinNoise(u * 5f + 11f, v * 5f + 7f) * 0.6f + Mathf.PerlinNoise(u * 13f, v * 13f + 3f) * 0.4f;
                        float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * Size;
                        float shade = 0.84f + (fibres - 0.5f) * 0.12f + (print - 0.5f) * 0.1f;
                        if (edge < 3.5f) shade = 0.22f;                 // inked cut edge
                        else if (edge < 9f) shade += 0.08f;            // light bevel just inside it
                        float h = edge < 3.5f ? 0f : Mathf.Min(1f, edge / 9f) * 0.8f + fibres * 0.2f;
                        heights[y * Size + x] = h;
                        byte grey = (byte)(Mathf.Clamp01(shade) * 255f);
                        colors[y * Size + x] = new Color32(grey, grey, grey, 255);
                    }
                }

                cardboardTexture = Texture(colors, "Cardboard", false);
                cardboardNormal = NormalFrom(heights, 2.5f, "Cardboard Normal");
            }

            albedo = cardboardTexture;
            normal = cardboardNormal;
        }

        public static Material WoodMaterial
        {
            get
            {
                if (woodMaterial != null) return woodMaterial;
                woodMaterial = Lit(WoodTexture(), new Color(0.72f, 0.56f, 0.4f), 0.32f);
                woodMaterial.name = "Table Wood";
                return woodMaterial;
            }
        }

        private static Texture2D WoodTexture()
        {
            if (woodTexture != null) return woodTexture;
            var colors = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;
                    float warp = Mathf.PerlinNoise(u * 3f, v * 2f) * 6f;
                    float grain = Mathf.Sin((v * 22f + warp) * Mathf.PI) * 0.5f + 0.5f;
                    grain = Mathf.Pow(grain, 3f);
                    float streaks = Mathf.PerlinNoise(u * 40f, v * 3f);
                    float shade = 0.8f - grain * 0.14f + (streaks - 0.5f) * 0.12f;
                    Color c = new Color(1f, 0.9f, 0.8f) * shade;
                    colors[y * Size + x] = c;
                }
            }

            woodTexture = Texture(colors, "Wood", false);
            return woodTexture;
        }

        private static Texture2D BoardTexture()
        {
            var colors = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * Size;
                    float felt = 0.9f + (Mathf.PerlinNoise(u * 80f, v * 80f) - 0.5f) * 0.15f;
                    Color c = new Color(0.16f, 0.27f, 0.22f) * felt;
                    if (edge < 2f) c = new Color(0.05f, 0.05f, 0.06f);
                    else if (edge > 5f && edge < 7f) c = new Color(0.85f, 0.68f, 0.3f);
                    colors[y * Size + x] = c;
                }
            }

            return Texture(colors, "Game Board", false, TextureWrapMode.Clamp);
        }

        private static Texture2D CardTexture(Color back, Color trim)
        {
            var colors = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * Size;
                    Color c = back * (0.9f + (Mathf.PerlinNoise(u * 30f, v * 30f) - 0.5f) * 0.1f);
                    if (edge < 10f) c = new Color(0.96f, 0.94f, 0.88f);
                    else if (edge < 14f) c = trim;
                    float diamond = Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f);
                    if (Mathf.Abs(diamond - 0.22f) < 0.012f || diamond < 0.08f) c = trim;
                    colors[y * Size + x] = c;
                }
            }

            return Texture(colors, "Card", false, TextureWrapMode.Clamp);
        }

        private static Texture2D DieTexture()
        {
            var colors = new Color32[Size * Size];
            Vector2[] pips = { new Vector2(0.5f, 0.5f), new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.75f), new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.25f) };
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2(x / (float)Size, y / (float)Size);
                    float edge = Mathf.Min(Mathf.Min(p.x, 1f - p.x), Mathf.Min(p.y, 1f - p.y));
                    Color c = edge < 0.03f ? new Color(0.75f, 0.73f, 0.7f) : Color.white;
                    foreach (Vector2 pip in pips)
                        if ((p - pip).magnitude < 0.085f) c = new Color(0.08f, 0.08f, 0.1f);
                    colors[y * Size + x] = c;
                }
            }

            return Texture(colors, "Die", false, TextureWrapMode.Clamp);
        }

        private static Texture2D Texture(Color32[] colors, string name, bool linear, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, linear) { name = name };
            texture.SetPixels32(colors);
            texture.wrapMode = wrap;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            texture.Apply(true, true);
            return texture;
        }

        private static Texture2D NormalFrom(float[] heights, float strength, string name)
        {
            var colors = new Color32[heights.Length];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float left = heights[y * Size + Mathf.Max(0, x - 1)];
                    float right = heights[y * Size + Mathf.Min(Size - 1, x + 1)];
                    float down = heights[Mathf.Max(0, y - 1) * Size + x];
                    float up = heights[Mathf.Min(Size - 1, y + 1) * Size + x];
                    Vector3 n = new Vector3((left - right) * strength, (down - up) * strength, 1f).normalized;
                    colors[y * Size + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f),
                        (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
                }
            }

            return Texture(colors, name, true, TextureWrapMode.Clamp);
        }

        private static Material Lit(Texture texture, Color color, float smoothness)
        {
            var material = new Material(BattleEnvironment.LitShader);
            if (texture != null) material.mainTexture = texture;
            material.color = color;
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", 0f);
            return material;
        }

        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Vector3 euler, Material material) =>
            Primitive(PrimitiveType.Cube, parent, name, position, scale, euler, material, false);

        private static GameObject Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Vector3 euler,
            Material material, bool local = false) =>
            Primitive(PrimitiveType.Cylinder, parent, name, position, scale, euler, material, local);

        private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale,
            Vector3 euler, Material material, bool local)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name;
            Object.Destroy(item.GetComponent<Collider>());
            item.transform.SetParent(parent, false);
            if (local)
            {
                item.transform.localPosition = position;
                item.transform.localRotation = Quaternion.Euler(euler);
            }
            else
            {
                item.transform.position = position;
                item.transform.rotation = Quaternion.Euler(euler);
            }

            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item;
        }
    }
}
