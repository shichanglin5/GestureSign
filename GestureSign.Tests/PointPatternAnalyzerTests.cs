using GestureSign.PointPatterns;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;

namespace GestureSign.Tests
{
    [TestClass]
    public class PointPatternAnalyzerTests
    {
        [TestMethod]
        public void GetPointPatternMatchResults_WhenLoopAndZigZagAreCompared_PrefersLoopForELikeInput()
        {
            var analyzer = new PointPatternAnalyzer(new[]
            {
                new PointsPatternSet("z", CreateZLikePattern()),
                new PointsPatternSet("e", CreateELikePattern()),
            });

            var results = analyzer.GetPointPatternMatchResults(CreateELikeVariant())
                .ToDictionary(result => result.Name, result => result.Probability);

            Assert.IsTrue(results["e"] > results["z"],
                $"Expected e-like input to match loop pattern better. e={results["e"]:F2}, z={results["z"]:F2}");
            Assert.IsTrue(results["e"] - results["z"] >= 8d,
                $"Expected structural penalty to widen the separation between e and z. e={results["e"]:F2}, z={results["z"]:F2}");
        }

        private static Point[] CreateZLikePattern()
        {
            return new[]
            {
                new Point(0, 0),
                new Point(18, 0),
                new Point(36, 0),
                new Point(28, 8),
                new Point(20, 16),
                new Point(12, 24),
                new Point(4, 32),
                new Point(20, 32),
                new Point(36, 32),
            };
        }

        private static Point[] CreateELikePattern()
        {
            return new[]
            {
                new Point(30, 6),
                new Point(20, 2),
                new Point(10, 4),
                new Point(4, 14),
                new Point(4, 26),
                new Point(10, 36),
                new Point(22, 40),
                new Point(32, 36),
                new Point(36, 26),
                new Point(30, 18),
                new Point(20, 16),
                new Point(12, 18),
                new Point(20, 20),
                new Point(30, 18),
                new Point(36, 10),
            };
        }

        private static Point[] CreateELikeVariant()
        {
            return new[]
            {
                new Point(28, 8),
                new Point(18, 4),
                new Point(9, 6),
                new Point(3, 16),
                new Point(4, 28),
                new Point(10, 38),
                new Point(22, 42),
                new Point(33, 38),
                new Point(37, 28),
                new Point(31, 19),
                new Point(21, 16),
                new Point(13, 17),
                new Point(21, 21),
                new Point(31, 19),
                new Point(37, 12),
            };
        }

        #region IsTapGesture Tests

        [TestMethod]
        public void IsTapGesture_SinglePoint_ReturnsTrue()
        {
            var analyzer = new PointPatternAnalyzer { TapThreshold = 50 };
            var tap = new PointsPatternSet("tap", new[] { new Point(100, 100) });
            var saved = new PointsPatternSet("saved", new[] { new Point(0, 0) });
            var result = analyzer.GetPointPatternMatchResult(saved, tap);
            // Single point is always a tap → both tap → 100%
            Assert.AreEqual(100d, result.Probability);
        }

        [TestMethod]
        public void IsTapGesture_JitterWithinThreshold_StillTap()
        {
            // Finger jitters: 0 → +10 → -10 → 0 (cumulative distance = 30, max displacement = 10)
            // With cumulative distance, this would exceed a threshold of 25 and falsely be classified as swipe
            var analyzer = new PointPatternAnalyzer { TapThreshold = 25 };
            var jitter = new[] { new Point(100, 100), new Point(110, 100), new Point(90, 100), new Point(100, 100) };
            var tapPoints = new[] { new Point(0, 0) };
            var result = analyzer.GetPointPatternMatchResult(
                new PointsPatternSet("saved", tapPoints),
                new PointsPatternSet("input", jitter));
            Assert.AreEqual(100d, result.Probability,
                "Jittery finger with max displacement 10 should be tap when threshold is 25");
        }

        [TestMethod]
        public void IsTapGesture_DisplacementExceedsThreshold_NotTap()
        {
            var analyzer = new PointPatternAnalyzer { TapThreshold = 25 };
            var swipe = new[] { new Point(100, 100), new Point(130, 100) };
            var tapPoints = new[] { new Point(0, 0) };
            var result = analyzer.GetPointPatternMatchResult(
                new PointsPatternSet("saved", tapPoints),
                new PointsPatternSet("input", swipe));
            // Displacement 30 > threshold 25 → not a tap, saved is tap → mismatch → 0%
            Assert.AreEqual(0d, result.Probability,
                "Movement of 30px should not be classified as tap when threshold is 25");
        }

        #endregion
    }
}
