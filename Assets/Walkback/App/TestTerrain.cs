// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;
using UnityEngine.Rendering;

namespace Walkback
{
    /// <summary>
    /// TODO(H1): replace with the real Gale terrain from Assets/StreamingAssets/Site/ (session B).
    /// A synthetic surface with the same shape as the data contract (257 x 257 nodes over 1 km,
    /// row 0 = north, x east, y north) so the scene, the table and the test exist before the data.
    /// It is NOT Mars data and the panel says so while it is on screen.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TestTerrain : MonoBehaviour
    {
        public const int GridSize = 257;
        public const float SideMeters = 1000f;

        public float MinElevation { get; private set; }
        public float MaxElevation { get; private set; }
        public float[] Heights { get; private set; }

        Mesh mesh;
        Texture2D paint;

        void Awake()
        {
            Build();
        }

        void OnDestroy()
        {
            DestroySafe(mesh);
            DestroySafe(paint);
        }

        /// <summary>Synthetic height in meters at local (x east, y north), both 0..1000.</summary>
        public static float HeightAt(float x, float y)
        {
            float h = 40f * Mathf.Sin(x / 260f) * Mathf.Cos(y / 310f);
            h += 25f * Mathf.Sin((x + y) / 90f);
            // A north-south ridge with a pass in the middle.
            float ridge = 60f * Mathf.Exp(-Sq(x - 650f) / (2f * Sq(60f)));
            float pass = 1f - 0.8f * Mathf.Exp(-Sq(y - 500f) / (2f * Sq(70f)));
            h += ridge * pass;
            // A shallow bowl.
            h -= 30f * Mathf.Exp(-(Sq(x - 300f) + Sq(y - 300f)) / (2f * Sq(80f)));
            return h;
        }

        public void Build()
        {
            int n = GridSize;
            float step = SideMeters / (n - 1);
            Heights = new float[n * n];
            float min = float.MaxValue, max = float.MinValue;
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    float h = HeightAt(c * step, SideMeters - r * step);
                    Heights[r * n + c] = h;
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }
            MinElevation = min;
            MaxElevation = max;

            var vertices = new Vector3[n * n];
            var uv = new Vector2[n * n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    // Mesh units are meters; the parent scales the square kilometer down to the table.
                    vertices[i] = new Vector3(c * step - SideMeters / 2f, Heights[i] - min, SideMeters / 2f - r * step);
                    uv[i] = new Vector2(c / (float)(n - 1), 1f - r / (float)(n - 1));
                }
            }

            var triangles = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int r = 0; r < n - 1; r++)
            {
                for (int c = 0; c < n - 1; c++)
                {
                    int a = r * n + c, b = a + 1, d = a + n, e = d + 1;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = e;
                    triangles[t++] = a; triangles[t++] = e; triangles[t++] = d;
                }
            }

            DestroySafe(mesh);
            mesh = new Mesh { name = "TestTerrain", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            DestroySafe(paint);
            paint = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "TestTerrainPaint", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[n * n];
            Color low = new Color(0.36f, 0.24f, 0.17f), high = new Color(0.88f, 0.72f, 0.54f);
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    float k = Mathf.InverseLerp(min, max, Heights[r * n + c]);
                    // Texture row 0 is the bottom (south); grid row 0 is the north.
                    pixels[(n - 1 - r) * n + c] = Color.Lerp(low, high, k);
                }
            }
            paint.SetPixels32(pixels);
            paint.Apply(false);

            var block = new MaterialPropertyBlock();
            block.SetTexture("_BaseMap", paint);
            GetComponent<MeshRenderer>().SetPropertyBlock(block);
        }

        static float Sq(float v) { return v * v; }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
