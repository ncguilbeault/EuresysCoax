using OpenCV.Net;
using Euresys;
using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Drawing.Design;

namespace EuresysCoax
{
    // Create a description for the Euresys Frame Grabber Bonsai node
    [Description("Produces a sequence of images acquired from a coaxpress camera connected to the Euresys Coaxlink frame grabber card (Euresys Inc.).")]

    public class EuresysCoaxlinkGrabber2 : Source<IplImage>
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

        // Path to the saved camera state file
        [Editor("Bonsai.Design.OpenFileNameEditor, Bonsai.Design", typeof(UITypeEditor))]
        [Description("The path to the file containing the saved camera settings. Optional.")]
        public string SettingsFilePath { get; set; }

        public override IObservable<IplImage> Generate()
        {
            return source;
        }

        private IObservable<IplImage> source;
        private readonly object captureLock = new object();
        private IObserver<IplImage> global_observer;

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

        public EuresysCoaxlinkGrabber2()
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
                        using (Euresys.GenTL genTL = new Euresys.GenTL())
                        {
                            using (EGrabberCallbackOnDemand grabber = new EGrabberCallbackOnDemand(genTL))
                            {
                                using (Euresys.FormatConverter.FormatConverter converter = new Euresys.FormatConverter.FormatConverter(genTL))
                                {
                                    grabber.reallocBuffers(BufferCount);
                                    ulong width = grabber.getWidth();
                                    ulong height = grabber.getHeight();
                                    grabber.enableNewBufferDataEvent();
                                    grabber.onNewBufferEvent = delegate (EGrabberCallbackOnDemand g, NewBufferData data)
                                    {
                                        using (ScopedBuffer buffer = new ScopedBuffer(g, data))
                                        {
                                            IntPtr bufferPtr;
                                            IplImage output;
                                            buffer.getInfo(Euresys.gc.BUFFER_INFO_CMD.BUFFER_INFO_BASE, out bufferPtr);
                                            output = new IplImage(new Size((int)width, (int)height), IplDepth.U8, 1, bufferPtr).Clone();
                                            global_observer.OnNext(output);
                                        }
                                    };
                                    EGrabberCallbacksWorker worker = new EGrabberCallbacksWorker(grabber);
                                    System.Threading.Thread workerThread = new System.Threading.Thread(worker.DoWork);
                                    workerThread.Start();

                                    grabber.start();

                                    while (!cancellationToken.IsCancellationRequested)
                                    {
                                        // Wait for cancellation.
                                    }
                                    grabber.stop();
                                    worker.RequestStop();
                                    workerThread.Join();
                                    grabber.disableAllEvent();
                                }
                            }
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