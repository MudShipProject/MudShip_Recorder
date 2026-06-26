using System.Collections.Generic;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 記録対象のスケルトン定義。指定した root Transform 以下を決定的順序 (深さ優先) で
    /// 走査して得たボーン配列と、その root 相対パス、位置も記録するボーンの一覧を保持する。
    ///
    /// Humanoid 限定ではなく root ベース全走査を採るため、髪・スカート・アクセサリ等の
    /// 追加ボーンも漏れなく記録でき、汎用リグ・非ヒューマノイドにも対応する。
    /// </summary>
    public sealed class SkeletonDefinition
    {
        /// <summary>走査の基準となった root。BoneTable のパスはこの root 相対 (root 名は含めない)。</summary>
        public readonly Transform Root;

        /// <summary>記録対象ボーン (走査順)。インデックスがフォーマット内の並び順を規定する。読み取り専用として扱うこと。</summary>
        public readonly Transform[] Bones;

        /// <summary><see cref="Bones"/> と同順の root 相対パス。</summary>
        public readonly string[] Paths;

        /// <summary>localPosition も記録するボーンの <see cref="Bones"/> インデックス (通常は Hip の 1 個)。</summary>
        public int[] PositionBoneIndices;

        public SkeletonDefinition(Transform root, Transform[] bones, string[] paths, int[] positionBoneIndices)
        {
            Root = root;
            Bones = bones;
            Paths = paths;
            PositionBoneIndices = positionBoneIndices;
        }

        /// <summary>1フレームのバイト数。</summary>
        public int Stride => MsrcFormat.ComputeStride(Bones.Length, PositionBoneIndices.Length);

        /// <summary>
        /// root 以下の全 Transform を走査してスケルトン定義を構築する。
        /// </summary>
        /// <param name="root">走査の基準。</param>
        /// <param name="positionBones">localPosition も記録するボーン群 (root 配下に含まれるもののみ採用)。null/空なら位置記録なし。</param>
        /// <param name="includeRoot">root 自身もボーンに含めるか (既定 false)。</param>
        public static SkeletonDefinition FromHierarchy(
            Transform root, IReadOnlyList<Transform> positionBones = null, bool includeRoot = false)
        {
            if (root == null)
                throw new System.ArgumentNullException(nameof(root));

            var bones = new List<Transform>(128);
            var paths = new List<string>(128);
            Collect(root, root, includeRoot, bones, paths);

            var indexOf = new Dictionary<Transform, int>(bones.Count);
            for (int i = 0; i < bones.Count; i++)
                indexOf[bones[i]] = i;

            var posIdx = new List<int>(2);
            if (positionBones != null)
            {
                foreach (var t in positionBones)
                    if (t != null && indexOf.TryGetValue(t, out int idx))
                        posIdx.Add(idx);
            }

            return new SkeletonDefinition(root, bones.ToArray(), paths.ToArray(), posIdx.ToArray());
        }

        /// <summary>
        /// Animator からスケルトン定義を構築する。root は Animator の Transform。
        /// 腰は <see cref="FromAnimator(Animator, Transform, IReadOnlyList{Transform}, bool)"/> に委譲し、
        /// 明示指定が無ければ Humanoid の Hips を自動採用する。
        /// </summary>
        public static SkeletonDefinition FromAnimator(Animator animator, bool includeRoot = false)
            => FromAnimator(animator, null, null, includeRoot);

        /// <summary>
        /// Animator からスケルトン定義を構築する。root は Animator の Transform。
        /// localPosition を記録するボーンは次の順で集める:
        /// <list type="number">
        /// <item><b>腰</b>: <paramref name="hipBone"/> があればそれ。無指定かつ Humanoid なら Hips を自動採用。
        /// 非 Humanoid で無指定なら腰は記録しない (root ボーンを勝手に拾うことはしない)。</item>
        /// <item><b>追加</b>: <paramref name="extraPositionBones"/> (ツイストボーン等、腰以外で位置も要るもの)。</item>
        /// </list>
        /// root 配下に無いボーンは <see cref="FromHierarchy"/> 側で除外される。重複は除く。
        /// </summary>
        /// <param name="animator">記録対象。Transform 以下を全走査して回転を記録する。</param>
        /// <param name="hipBone">localPosition を記録する腰ボーン。null なら Humanoid の Hips を自動。</param>
        /// <param name="extraPositionBones">腰に加えて localPosition を記録する追加ボーン群 (任意)。</param>
        /// <param name="includeRoot">root 自身もボーンに含めるか (既定 false)。</param>
        public static SkeletonDefinition FromAnimator(
            Animator animator, Transform hipBone, IReadOnlyList<Transform> extraPositionBones,
            bool includeRoot = false)
        {
            if (animator == null)
                throw new System.ArgumentNullException(nameof(animator));

            var posBones = new List<Transform>(4);
            var seen = new HashSet<Transform>();

            // 1) 腰: 明示指定 → 無ければ Humanoid の Hips を自動採用。
            //    非 Humanoid で無指定なら腰は記録しない (意味のない root 定数データを避ける)。
            Transform hip = hipBone;
            if (hip == null && animator.isHuman)
                hip = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hip != null && seen.Add(hip))
                posBones.Add(hip);

            // 2) 追加の位置記録ボーン (ツイストボーン等)。
            if (extraPositionBones != null)
            {
                foreach (var t in extraPositionBones)
                    if (t != null && seen.Add(t))
                        posBones.Add(t);
            }

            return FromHierarchy(animator.transform, posBones, includeRoot);
        }

        static void Collect(Transform root, Transform node, bool includeRoot,
            List<Transform> bones, List<string> paths)
        {
            if (node != root || includeRoot)
            {
                bones.Add(node);
                paths.Add(BuildRelativePath(root, node));
            }

            int n = node.childCount;
            for (int i = 0; i < n; i++)
                Collect(root, node.GetChild(i), includeRoot, bones, paths);
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
