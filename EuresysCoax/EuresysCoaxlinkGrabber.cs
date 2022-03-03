using OpenCV.Net;
using Euresys;
using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace EuresysCoax
{
    // Create a description for the Euresys Frame Grabber Bonsai node
    [Description("Produces a sequence of images acquired from a coaxpress camera connected to the Euresys Coaxlink frame grabber card (Euresys Inc.).")]

    public class EuresysCoaxlinkGrabber : Source<IplImage>
    {
        // Coaxlink card index
        [Description("The index of the Euresys Coaxlink frame grabber card.")]
        public int CardIdx { get; set; }

        // Coax device index
        [Description("The index of the coaxlink device.")]
        public int DeviceIdx { get; set; }

        // Buffer allocation
        [Description("The number of buffers to allocate to image acquisition.")]
        public uint Buffers { get; set; }

        public override IObservable<IplImage> Generate()
        {
            return source;
        }

        private IObservable<IplImage> source;
        private readonly object captureLock = new object();
        private IObserver<IplImage> global_observer;
        private long width;
        private long height;
        private EGrabberCallbackOnDemand grabber = null;
        private EGenTL gentl = null;
        private bool stopping, rendering, disposed;

        public void onNewBuffer(EGrabberCallbackOnDemand g, NewBufferData data)
        {
            Console.WriteLine("Here2");
            //ulong timestamp = data.timestamp;
            //Console.WriteLine("timestamp={0}", timestamp);
            Console.WriteLine("stopping={0} disposed={1} rendering={2}", stopping, disposed, rendering);
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
                try
                {
                    if (stopping || disposed)
                    {
                        return;
                    }
                    using (ScopedBuffer buffer = new ScopedBuffer(g, data))
                    {
                        IntPtr bufferPtr;
                        IplImage output;
                        unsafe
                        {
                            buffer.getInfo(Euresys.gc.BUFFER_INFO_CMD.BUFFER_INFO_BASE, out bufferPtr);
                            output = new IplImage(new Size((int)width, (int)height), IplDepth.U8, 1, data.userPointer).Clone();
                        }
                        global_observer.OnNext(output);
                    }
                }
                finally
                {
                    rendering = false;
                }
            }
        }

        public EuresysCoaxlinkGrabber()
        {
            CardIdx = 0;
            DeviceIdx = 0;
            Buffers = 10;

            source = Observable.Create<IplImage>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    lock (captureLock)
                    {
                        global_observer = observer;
                        //Console.WriteLine("Here");
                        gentl = new EGenTL();
                        grabber = new EGrabberCallbackOnDemand(gentl, CardIdx, DeviceIdx);
                        //grabber.runScript("config.js");
                        string card = grabber.getStringInterfaceModule("InterfaceID");
                        string device = grabber.getStringDeviceModule("DeviceID");
                        int nFrame = 0;

                        height = grabber.getIntegerRemoteModule("Height");
                        width = grabber.getIntegerRemoteModule("Width");

                        Console.WriteLine("Interface: {0}", card);
                        Console.WriteLine("Device: {0}", device);
                        Console.WriteLine("Resolution: {0}x{1}", width, height);

                        //grabber.onNewBufferEvent = delegate (EGrabberCallbackOnDemand g, NewBufferData data) {  onNewBufferEventCallback(data); };
                        //grabber.onNewBufferEvent = delegate (EGrabberCallbackOnDemand g, NewBufferData data) 
                        //{
                        //    Console.WriteLine("timestamp: {0} us", data.timestamp);
                        //};

                        //grabber.enableNewBufferDataEvent();
                        //grabber.enableAllEvent();
                        grabber.reallocBuffers(Buffers);

                        //Console.WriteLine(height);
                        //Console.WriteLine(width);
                        //grabber.enableNewBufferDataEvent();
                        //grabber.runScript("restore-state.js");
                        try
                        {
                            Console.WriteLine("Here0");
                            stopping = false;
                            disposed = false;
                            rendering = false;
                            grabber.onNewBufferEvent = onNewBuffer;
                            grabber.start();
                            Console.WriteLine("Here1");
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                // Wait for cancellation.
                            }
                            Console.WriteLine("Here3");
                            stopping = true;
                            grabber.stop();
                            grabber.flushAllEvent();
                            grabber.onNewBufferEvent = null;
                        }
                        finally
                        {
                            if (grabber != null)
                            {
                                grabber.Dispose();
                                grabber = null;
                            }
                            if (gentl != null)
                            {
                                gentl.Dispose();
                                gentl = null;
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