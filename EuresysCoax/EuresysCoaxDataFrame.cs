using OpenCV.Net;
using System.ComponentModel;

namespace EuresysCoax
{
    public class EuresysCoaxDataFrame
    {
        // Timestamp
        [Description("The timestamp in which the image was captured.")]
        public ulong Timestamp { get; set; }

        // Image
        [Description("The image captured by the Euresys Coaxlink frame grabber card.")]
        public IplImage Image { get; set; }

        public EuresysCoaxDataFrame(IplImage image, ulong timestamp)
        {
            Image = image;
            Timestamp = timestamp;
        }
    }
}
