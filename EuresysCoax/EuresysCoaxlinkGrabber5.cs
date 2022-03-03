using OpenCV.Net;
using Euresys;
using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EuresysCoax
{
    // Create a description for the Euresys Frame Grabber Bonsai node
    [Description("Produces a sequence of images acquired from a coaxpress camera connected to the Euresys Coaxlink frame grabber card (Euresys Inc.).")]

    public class EuresysCoaxlinkGrabber5 : Source<EuresysCoaxDataFrame>
    {
        // Coaxlink card index
        [Description("The index of the Euresys Coaxlink frame grabber card.")]
        public int CardIdx { get; set; }

        // Coax device index
        [Description("The index of the coaxlink device.")]
        public int DeviceIdx { get; set; }

        // Buffer allocation
        [Description("The number of buffers to allocate to image acquisition.")]
        public uint BufferCount { get; set; }

        public EuresysCoaxlinkGrabber5()
        {
            CardIdx = 0;
            DeviceIdx = 0;
            BufferCount = 10;
        }

        public override IObservable<EuresysCoaxDataFrame> Generate()
        {
            return Observable.Create<EuresysCoaxDataFrame>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    using (Euresys.GenTL genTL = new Euresys.GenTL())
                    {
                        using (ManualResetEvent waitHandle = new ManualResetEvent(false))
                        {
                            using (var notification = cancellationToken.Register(() => waitHandle.Set()))
                            {
                                using (EGrabberCallbackOnDemand grabber = new EGrabberCallbackOnDemand(genTL))
                                {
                                    grabber.reallocBuffers(BufferCount);
                                    int width = (int)grabber.getIntegerRemoteModule("Width");
                                    int height = (int)grabber.getIntegerRemoteModule("Height");
                                    IplImage output = new IplImage(new Size(width, height), IplDepth.U8, 1);
                                    bool rendering = false;
                                    grabber.enableNewBufferDataEvent();
                                    grabber.onNewBufferEvent = delegate (EGrabberCallbackOnDemand g, NewBufferData data)
                                    {
                                        if (rendering)
                                        {
                                            g.push(data);
                                        }
                                        else
                                        {
                                            rendering = true;
                                            using (ScopedBuffer buffer = new ScopedBuffer(g, data))
                                            {
                                                unsafe
                                                {
                                                    buffer.getInfo(Euresys.gc.BUFFER_INFO_CMD.BUFFER_INFO_BASE, out IntPtr bufferPtr);
                                                    output.SetData(bufferPtr, output.WidthStep);
                                                    observer.OnNext(new EuresysCoaxDataFrame(output, data.timestamp));
                                                }
                                                rendering = false;
                                            }
                                        }
                                    };
                                    grabber.start();
                                    while (!cancellationToken.IsCancellationRequested)
                                    {
                                       grabber.processEventFilter(EventSelector.NewBufferData);
                                    }
                                    output.SetHandleAsInvalid();
                                    grabber.stop();
                                    grabber.cancelEventFilter(EventSelector.NewBufferData);
                                    grabber.disableAllEvent();
                                }
                            }
                        }
                    }
                });
            });
        }
    }
}