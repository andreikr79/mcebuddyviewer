using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.MediaCenter.UI;

namespace MceBuddyViewer
{
    /// <summary>
    /// Transformer for converting one or more fractional values into a Vector3 representation.
    /// </summary>
    public class VectorTransformer : ITransformer
    {

        /// <summary>
        /// Format string that dictates which dimension of the vector is
        /// represented by the value, e.g. "{0},1,1". The remaining dimensions
        /// are taken from the format string itself.
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Transforms the input value into a Vector3 representation.
        /// </summary>
        /// <param name="value">Input value.</param>
        /// <returns>Output value.</returns>
        public object Transform(object value)
        {
            string[] parts = String.Format(Format, value).Split(';');
            return new Vector3(
                Single.Parse(parts[0]),
                Single.Parse(parts[1]),
                Single.Parse(parts[2])
            );
        }
    }
}
