using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OCRLessonReport.Imaging
{
    public class HoughLineRequestSettings
    {
        public bool HorizontalLines { get; set; }
        public bool VerticalLines { get; set; }

        public int HorizontalDeviation { get; set; }
        public int VerticalDeviation { get; set; }        

        private HashSet<int> allowedThetas;

        public HashSet<int> AllowedThetas        
        {
            get
            {
                if (allowedThetas == null)
                    allowedThetas = GetAllowedThetas();

                return allowedThetas;

            }
        }

        private HashSet<int> GetAllowedThetas()
        {            
            IEnumerable<int> hRanges = new List<int>();
            IEnumerable<int> vRanges = new List<int>();

            if (VerticalLines)
            {
                var count = (VerticalDeviation != 0) ? VerticalDeviation : 1;
                vRanges = Enumerable.Range(0, count).Concat(Enumerable.Range(180 - VerticalDeviation, count)).ToList();
            }

            if (HorizontalLines)
                hRanges = Enumerable.Range(90 - HorizontalDeviation, (HorizontalDeviation != 0) ? HorizontalDeviation * 2 : 1).ToList();

            var thetas = new HashSet<int>(hRanges.Concat(vRanges));

            return thetas;
        }
    }
}
