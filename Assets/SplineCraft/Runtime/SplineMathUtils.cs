using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace SplineCraft
{
    /// <summary>
    /// Pure math utilities for spline sampling. No MonoBehaviour dependencies.
    /// All methods are stateless — same inputs always produce same outputs.
    /// </summary>
    public static class SplineMathUtils
    {
        /// <summary>
        /// Precomputed arc-length lookup table for a spline.
        /// Use <see cref="Build"/> to create, then query with <see cref="DistanceToT"/> / <see cref="TToDistance"/>.
        /// </summary>
        public class ArcLengthTable
        {
            readonly float[] _distances;  // cumulative arc lengths at each sample
            readonly float[] _ts;         // normalized T values at each sample
            public float TotalLength => _distances[_distances.Length - 1];

            internal ArcLengthTable(float[] distances, float[] ts)
            {
                _distances = distances;
                _ts = ts;
            }

            /// <summary>Converts arc-length distance to normalized spline T via linear interpolation.</summary>
            public float DistanceToT(float distance)
            {
                distance = Mathf.Clamp(distance, 0f, TotalLength);
                int lo = BinarySearchLower(_distances, distance);
                if (lo >= _distances.Length - 1) return _ts[_ts.Length - 1];
                float t = Mathf.InverseLerp(_distances[lo], _distances[lo + 1], distance);
                return Mathf.Lerp(_ts[lo], _ts[lo + 1], t);
            }

            /// <summary>Converts normalized spline T to arc-length distance via linear interpolation.</summary>
            public float TToDistance(float normalizedT)
            {
                normalizedT = Mathf.Clamp01(normalizedT);
                int lo = BinarySearchLower(_ts, normalizedT);
                if (lo >= _ts.Length - 1) return _distances[_distances.Length - 1];
                float t = Mathf.InverseLerp(_ts[lo], _ts[lo + 1], normalizedT);
                return Mathf.Lerp(_distances[lo], _distances[lo + 1], t);
            }

            static int BinarySearchLower(float[] arr, float value)
            {
                int lo = 0, hi = arr.Length - 1;
                while (lo < hi - 1)
                {
                    int mid = (lo + hi) / 2;
                    if (arr[mid] <= value) lo = mid; else hi = mid;
                }
                return lo;
            }
        }

        /// <summary>
        /// A rotation-minimizing frame (position + tangent + up + right) at a point on the spline.
        /// Uses double-reflection RMF to avoid the flipping/twisting of naive Frenet frames.
        /// </summary>
        public struct SplineFrame
        {
            public Vector3 Position;
            public Vector3 Tangent;
            public Vector3 Up;
            public Vector3 Right;
        }

        /// <summary>
        /// Builds an arc-length lookup table by sampling the spline at <paramref name="sampleCount"/> points.
        /// More samples = more accurate distance queries on highly curved splines.
        /// </summary>
        public static ArcLengthTable Build(ISpline spline, int sampleCount = 512)
        {
            sampleCount = Mathf.Max(sampleCount, 2);
            var distances = new float[sampleCount];
            var ts = new float[sampleCount];

            Vector3 prev = (Vector3)spline.EvaluatePosition(0f);
            distances[0] = 0f;
            ts[0] = 0f;

            for (int i = 1; i < sampleCount; i++)
            {
                float t = (float)i / (sampleCount - 1);
                Vector3 pos = (Vector3)spline.EvaluatePosition(t);
                distances[i] = distances[i - 1] + Vector3.Distance(prev, pos);
                ts[i] = t;
                prev = pos;
            }

            return new ArcLengthTable(distances, ts);
        }

        /// <summary>
        /// Computes RMF frames along the spline at evenly-spaced normalized T values.
        /// The first frame's up vector is seeded from the spline's initial tangent cross-product.
        /// </summary>
        public static SplineFrame[] ComputeRMFFrames(ISpline spline, int frameCount)
        {
            frameCount = Mathf.Max(frameCount, 2);
            var frames = new SplineFrame[frameCount];

            // Seed first frame
            Vector3 t0 = ((Vector3)spline.EvaluateTangent(0f)).normalized;
            Vector3 seedUp = Mathf.Abs(Vector3.Dot(t0, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
            Vector3 r0 = Vector3.Cross(t0, seedUp).normalized;
            Vector3 u0 = Vector3.Cross(r0, t0).normalized;

            frames[0] = new SplineFrame
            {
                Position = (Vector3)spline.EvaluatePosition(0f),
                Tangent = t0,
                Up = u0,
                Right = r0
            };

            // Double-reflection propagation — avoids twist at inflection points
            for (int i = 1; i < frameCount; i++)
            {
                float tVal = (float)i / (frameCount - 1);
                Vector3 pos = (Vector3)spline.EvaluatePosition(tVal);
                Vector3 tan = ((Vector3)spline.EvaluateTangent(tVal)).normalized;

                frames[i] = PropagateFrame(frames[i - 1], pos, tan);
            }

            return frames;
        }

        /// <summary>
        /// Returns position, tangent, up, and right at arc-length distance <paramref name="distance"/>
        /// using the provided lookup table and RMF frames.
        /// </summary>
        public static SplineFrame SampleFrameAtDistance(
            ISpline spline, ArcLengthTable table, SplineFrame[] rmfFrames, float distance)
        {
            float t = table.DistanceToT(distance / table.TotalLength * table.TotalLength);
            return SampleFrameAtT(spline, rmfFrames, t);
        }

        /// <summary>
        /// Returns a frame at normalized T by interpolating between precomputed RMF frames.
        /// </summary>
        public static SplineFrame SampleFrameAtT(ISpline spline, SplineFrame[] rmfFrames, float t)
        {
            t = Mathf.Clamp01(t);
            float indexF = t * (rmfFrames.Length - 1);
            int lo = Mathf.FloorToInt(indexF);
            int hi = Mathf.Min(lo + 1, rmfFrames.Length - 1);
            float frac = indexF - lo;

            var a = rmfFrames[lo];
            var b = rmfFrames[hi];
            return new SplineFrame
            {
                Position = Vector3.Lerp(a.Position, b.Position, frac),
                Tangent  = Vector3.Slerp(a.Tangent, b.Tangent, frac).normalized,
                Up       = Vector3.Slerp(a.Up, b.Up, frac).normalized,
                Right    = Vector3.Slerp(a.Right, b.Right, frac).normalized
            };
        }

        /// <summary>
        /// Remaps a world-space distance within [startDist, endDist] to a normalized 0-1 T
        /// local to that sub-range. Clamps to [0,1].
        /// </summary>
        public static float RemapToSubRange(float worldDistance, float startDist, float endDist)
        {
            if (Mathf.Approximately(endDist, startDist)) return 0f;
            return Mathf.Clamp01((worldDistance - startDist) / (endDist - startDist));
        }

        /// <summary>
        /// Converts a sub-range normalized T (0-1 within [startDist, endDist]) to an arc-length
        /// world distance on the full spline.
        /// </summary>
        public static float SubRangeTToWorldDistance(float localT, float startDist, float endDist)
        {
            return Mathf.Lerp(startDist, endDist, Mathf.Clamp01(localT));
        }

        /// <summary>
        /// Returns the total arc length of the spline computed from the lookup table.
        /// </summary>
        public static float TotalLength(ArcLengthTable table) => table.TotalLength;

        /// <summary>
        /// Finds the closest point on the spline to <paramref name="worldPoint"/>,
        /// returning the normalized T and distance from the point to the spline.
        /// Uses coarse + fine sampling; accuracy controlled by <paramref name="coarseSamples"/>.
        /// </summary>
        public static float ClosestPointOnSpline(
            ISpline spline, Vector3 worldPoint, out float closestDist, int coarseSamples = 128)
        {
            float bestT = 0f;
            closestDist = float.MaxValue;

            for (int i = 0; i <= coarseSamples; i++)
            {
                float t = (float)i / coarseSamples;
                float d = Vector3.Distance((Vector3)spline.EvaluatePosition(t), worldPoint);
                if (d < closestDist) { closestDist = d; bestT = t; }
            }

            // Refine with binary search around the coarse best
            float step = 1f / coarseSamples;
            float lo = Mathf.Max(0f, bestT - step);
            float hi = Mathf.Min(1f, bestT + step);
            for (int i = 0; i < 8; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float dLo = Vector3.Distance((Vector3)spline.EvaluatePosition(lo), worldPoint);
                float dHi = Vector3.Distance((Vector3)spline.EvaluatePosition(hi), worldPoint);
                if (dLo < dHi) hi = mid; else lo = mid;
            }

            bestT = (lo + hi) * 0.5f;
            closestDist = Vector3.Distance((Vector3)spline.EvaluatePosition(bestT), worldPoint);
            return bestT;
        }

        // --- Private helpers ---

        static SplineFrame PropagateFrame(SplineFrame prev, Vector3 nextPos, Vector3 nextTan)
        {
            // Double-reflection RMF step
            Vector3 v1 = nextPos - prev.Position;
            float c1 = Vector3.Dot(v1, v1);

            Vector3 rL = c1 > 1e-10f
                ? prev.Right - (2f / c1) * Vector3.Dot(v1, prev.Right) * v1
                : prev.Right;
            Vector3 tL = c1 > 1e-10f
                ? prev.Tangent - (2f / c1) * Vector3.Dot(v1, prev.Tangent) * v1
                : prev.Tangent;

            Vector3 v2 = nextTan - tL;
            float c2 = Vector3.Dot(v2, v2);

            Vector3 right = c2 > 1e-10f
                ? rL - (2f / c2) * Vector3.Dot(v2, rL) * v2
                : rL;
            right = right.normalized;
            Vector3 up = Vector3.Cross(right, nextTan).normalized;

            return new SplineFrame
            {
                Position = nextPos,
                Tangent  = nextTan,
                Up       = up,
                Right    = right
            };
        }
    }
}
