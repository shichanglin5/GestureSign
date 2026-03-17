using GestureSign.Common.Gestures;
using GestureSign.PointPatterns;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GestureSign.Tests
{
    [TestClass]
    public class GestureManagerAssignmentTests
    {
        [TestMethod]
        public void FindBestTrajectoryAssignment_WhenMiddleTrajectoriesSwapped_ChoosesBestPermutation()
        {
            var matrix = new[]
            {
                new[]
                {
                    CreateResult(95),
                    CreateResult(12),
                    CreateResult(10),
                    CreateResult(8),
                },
                new[]
                {
                    CreateResult(10),
                    CreateResult(30),
                    CreateResult(90),
                    CreateResult(15),
                },
                new[]
                {
                    CreateResult(8),
                    CreateResult(88),
                    CreateResult(25),
                    CreateResult(14),
                },
                new[]
                {
                    CreateResult(9),
                    CreateResult(11),
                    CreateResult(13),
                    CreateResult(93),
                },
            };

            var assignment = GestureManager.FindBestTrajectoryAssignment(matrix);

            CollectionAssert.AreEqual(new[] { 0, 2, 1, 3 }, assignment);
        }

        [TestMethod]
        public void FindBestTrajectoryAssignment_WhenMatrixInvalid_ReturnsNull()
        {
            var invalidMatrix = new[]
            {
                new[] { CreateResult(80), CreateResult(20) },
                new[] { CreateResult(10) },
            };

            var assignment = GestureManager.FindBestTrajectoryAssignment(invalidMatrix);

            Assert.IsNull(assignment);
        }

        [TestMethod]
        public void GetFeatureFingerTrajectoryIndex_WhenOneOrTwoFingers_UsesFirstTrajectory()
        {
            Assert.AreEqual(0, GestureManager.GetFeatureFingerTrajectoryIndex(1));
            Assert.AreEqual(0, GestureManager.GetFeatureFingerTrajectoryIndex(2));
        }

        [TestMethod]
        public void GetFeatureFingerTrajectoryIndex_WhenThreeOrMoreFingers_UsesSecondTrajectory()
        {
            Assert.AreEqual(1, GestureManager.GetFeatureFingerTrajectoryIndex(3));
            Assert.AreEqual(1, GestureManager.GetFeatureFingerTrajectoryIndex(4));
        }

        private static PointPatternMatchResult CreateResult(double probability)
        {
            return new PointPatternMatchResult
            {
                Probability = probability,
                AngularProbability = probability,
                StructuralPenalty = 0,
            };
        }
    }
}
