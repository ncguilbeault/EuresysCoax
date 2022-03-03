using OpenCV.Net;
using Euresys;
using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Drawing.Design;
using System.Threading;

namespace EuresysCoax
{
    // Create a description for the Euresys Frame Grabber Bonsai node
    [Description("Produces a sequence of images acquired from a coaxpress camera connected to the Euresys Coaxlink frame grabber card (Euresys Inc.).")]

    public class EuresysCoaxlinkGrabber4 : Source<IplImage>
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

        public override IObservable<IplImage> Generate()
        {
            return source;
        }

        private IObservable<IplImage> source;
        private readonly object captureLock = new object();
        private IObserver<IplImage> global_observer;
        private IplImage output;
        private bool rendering, stopping, disposed = false;

        public EuresysCoaxlinkGrabber4()
        {
            CardIdx = 0;
            DeviceIdx = 0;
            BufferCount = 10;

            source = Observable.Create<IplImage>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    lock (captureLock)
                    {
                        global_observer = observer;
                        rendering = stopping = disposed = false;
                        using (Euresys.GenTL genTL = new Euresys.GenTL())
                        {
                            using (EGrabberCallbackOnDemand grabber = new EGrabberCallbackOnDemand(genTL))
                            {
                                grabber.reallocBuffers(BufferCount);
                                int width = (int)grabber.getIntegerRemoteModule("Width"); 
                                int height = (int)grabber.getIntegerRemoteModule("Height");
                                output = new IplImage(new Size(width, height), IplDepth.U8, 1);
                                grabber.enableNewBufferDataEvent();
                                using (ManualResetEvent waitHandle = new ManualResetEvent(false))
                                {
                                    using (var notification = cancellationToken.Register(() => waitHandle.Set()))
                                    {
                                        grabber.onNewBufferEvent = delegate (EGrabberCallbackOnDemand g, NewBufferData data)
                                        {
                                            try
                                            {
                                                if (stopping || disposed)
                                                {
                                                    return;
                                                }
                                                else if (rendering)
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
                                                            global_observer.OnNext(output);
                                                        }
                                                        rendering = false;
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                observer.OnError(ex);
                                                waitHandle.Set();
                                                throw;
                                            }
                                        };
                                        stopping = false;
                                        grabber.start();
                                        while (!cancellationToken.IsCancellationRequested)
                                        {
                                            // Wait for cancellation.
                                            grabber.processEventFilter(EventSelector.NewBufferData);
                                        }
                                        output.SetHandleAsInvalid();
                                        grabber.stop();
                                        grabber.cancelEventFilter(EventSelector.NewBufferData);
                                        stopping = true;
                                        grabber.disableAllEvent();
                                    }
                                }
                            }
                            disposed = true;
                        }
                    }
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            })
            .PublishReconnectable()
            .RefCount();
        }
    }
}