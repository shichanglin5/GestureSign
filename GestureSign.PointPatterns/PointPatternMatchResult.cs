using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GestureSign.PointPatterns
{
    public class PointPatternMatchResult
    {
        #region Constructors

        public PointPatternMatchResult()
        {

        }

        public PointPatternMatchResult(string Name, double Probability, int PointPatternSetCount) : this()
        {
            this.Name = Name;
            this.Probability = Probability;
        }

        #endregion

        #region Public Properties

        public string Name { get; set; }

        private double _Probability = 0;
        public double Probability
        {
            get { return _Probability; }
            set
            {
                _Probability = value;
            }
        }

        public double AngularProbability { get; set; }
        public double StructuralPenalty { get; set; }

        #endregion
    }
}
