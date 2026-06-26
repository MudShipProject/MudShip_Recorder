using System.Collections.Generic;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 表情記録対象の定義。指定 root 配下の <see cref="SkinnedMeshRenderer"/> 群と、その root 相対パス・
    /// 各 renderer の BlendShape 名一覧・総 BlendShape 数を保持する。
    ///
    /// 1 フレームでは全 renderer の全 BlendShape ウェイトを renderer 順 -> shape 順で記録する。
    /// </summary>
    public sealed class FaceDefinition
    {
        /// <summary>パスの基準 root (通常はキャラの Animator が付く Transform)。</summary>
        public readonly Transform Root;

        /// <summary>記録対象の SkinnedMeshRenderer 群 (記録順)。</summary>
        public readonly SkinnedMeshRenderer[] Renderers;

        /// <summary><see cref="Renderers"/> と同順の root 相対パス。</summary>
        public readonly string[] RendererPaths;

        /// <summary><see cref="Renderers"/> と同順の BlendShape 数。</summary>
        public readonly int[] ShapeCounts;

        /// <summary><see cref="Renderers"/> と同順の BlendShape 名配列。</summary>
        public readonly string[][] ShapeNames;

        /// <summary>全 renderer の BlendShape 総数 (= 1 フレームの weight 数)。</summary>
        public readonly int TotalShapeCount;

        public FaceDefinition(Transform root, SkinnedMeshRenderer[] renderers, string[] rendererPaths,
            int[] shapeCounts, string[][] shapeNames)
        {
            Root = root;
            Renderers = renderers;
            RendererPaths = rendererPaths;
            ShapeCounts = shapeCounts;
            ShapeNames = shapeNames;

            int total = 0;
            for (int i = 0; i < shapeCounts.Length; i++)
                total += shapeCounts[i];
            TotalShapeCount = total;
        }

        /// <summary>1 フレームのバイト数。</summary>
        public int Stride => MsrfFormat.ComputeStride(TotalShapeCount);

        /// <summary>記録対象が 1 つも無い (= 記録すべき BlendShape が無い) か。</summary>
        public bool IsEmpty => Renderers.Length == 0;

        /// <summary>
        /// <paramref name="root"/> 配下にある SkinnedMeshRenderer 群から定義を構築する。
        /// root 配下に無い / sharedMesh が無い / BlendShape 0 個 / 重複、の renderer は除外する。
        /// </summary>
        public static FaceDefinition FromRenderers(Transform root, IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            if (root == null)
                throw new System.ArgumentNullException(nameof(root));

            var rs = new List<SkinnedMeshRenderer>();
            var paths = new List<string>();
            var counts = new List<int>();
            var names = new List<string[]>();
            var seen = new HashSet<SkinnedMeshRenderer>();

            if (renderers != null)
            {
                foreach (var r in renderers)
                {
                    if (r == null || !seen.Add(r))
                        continue;

                    var mesh = r.sharedMesh;
                    if (mesh == null || mesh.blendShapeCount == 0)
                        continue;
                    if (!IsDescendantOf(r.transform, root))
                        continue;

                    int n = mesh.blendShapeCount;
                    var sn = new string[n];
                    for (int i = 0; i < n; i++)
                        sn[i] = mesh.GetBlendShapeName(i);

                    rs.Add(r);
                    paths.Add(BuildRelativePath(root, r.transform));
                    counts.Add(n);
                    names.Add(sn);
                }
            }

            return new FaceDefinition(root, rs.ToArray(), paths.ToArray(), counts.ToArray(), names.ToArray());
        }

        static bool IsDescendantOf(Transform t, Transform root)
        {
            var cur = t;
            while (cur != null)
            {
                if (cur == root)
                    return true;
                cur = cur.parent;
            }
            return false;
        }

        static string BuildRelativePath(Transform root, Transform t)
        {
            // root を含めず、t までの名前を '/' 連結する。
            var names = new List<string>(8);
            var cur = t;
            while (cur != null && cur != root)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
