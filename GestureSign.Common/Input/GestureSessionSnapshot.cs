using System;
using System.Collections.Generic;
using System.Drawing;

namespace GestureSign.Common.Input
{
    [Serializable]
    public class GestureSessionSnapshot
    {
        public int FingerCount { get; set; }
        public double DurationMs { get; set; }
        public List<int> ActiveContactIds { get; set; } = new List<int>();
        public List<int> ContactDownOrder { get; set; } = new List<int>();
        public List<int> ContactUpOrder { get; set; } = new List<int>();
        public Dictionary<int, double> ContactDownTimesMs { get; set; } = new Dictionary<int, double>();
        public Dictionary<int, double> ContactUpTimesMs { get; set; } = new Dictionary<int, double>();
        public bool HasPrimaryButtonClick { get; set; }
        public int PrimaryButtonFingerCount { get; set; }
        public double? PrimaryButtonDownTimeMs { get; set; }
        public double? PrimaryButtonUpTimeMs { get; set; }
        public Dictionary<int, List<Point>> ContactTrajectories { get; set; } = new Dictionary<int, List<Point>>();
        public List<List<Point>> AllPoints { get; set; } = new List<List<Point>>();
    }
}

