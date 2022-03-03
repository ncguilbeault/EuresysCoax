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

    public class EuresysCoaxlinkGrabber3 : Source<IplImage>
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

        public class EGrabberCallbacksWorker
        {
            public EGrabberCallbacksWorker(EGrabberCallbackOnDemand grabber)
            {
                this.grabber = grabber;
            }
            public void DoWork()
            {
                try
                {
                    while (!_shouldStop)
                    {
                        grabber.processEventFilter(EventSelector.NewBufferData);
                    }
                }
                catch (Exception e)
                {
                    System.Console.WriteLine("Exception = {0}", e.Message);
                }
            }
            public void RequestStop()
            {
                _shouldStop = true;
                grabber.cancelEventFilter(EventSelector.NewBufferData);
            }
            private volatile bool _shouldStop;
            EGrabberCallbackOnDemand grabber;
        }

        public EuresysCoaxlinkGrabber3()
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
                                grabber.onNewBufferEvent = delegate (EGrabberCallbackOnDemand g, NewBufferData data)
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
                                            buffer.getInfo(Euresys.gc.BUFFER_INFO_CMD.BUFFER_INFO_BASE, out IntPtr bufferPtr);
                                            output.SetData(bufferPtr, output.WidthStep);
                                            global_observer.OnNext(output);
                                            rendering = false;
                                        }
                                    }
                                };
                                EGrabberCallbacksWorker worker = new EGrabberCallbacksWorker(grabber);
                                Thread workerThread = new Thread(worker.DoWork);
                                workerThread.Start();
                                stopping = false;
                                grabber.start();

                                while (!cancellationToken.IsCancellationRequested)
                                {
                                    // Wait for cancellation.
                                    // grabber.processEventFilter(EventSelector.NewBufferData);
                                }
                                grabber.stop();
                                stopping = true;
                                worker.RequestStop();
                                workerThread.Join();
                                grabber.disableAllEvent();
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