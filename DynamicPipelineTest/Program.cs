using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Gst;
using Gst.WebRTC;
using Gst.Sdp;
using GLib;
using Gst.Audio;
using Timeout = GLib.Timeout;
using Value = GLib.Value;

// https://gstreamer.freedesktop.org/documentation/application-development/advanced/pipeline-manipulation.html?gi-language=c#dynamically-changing-the-pipeline

namespace GstreamerDynamicPipelineTest3
{
    static class Program
    {
        //static gchar *opt_effects = NULL;

        //#define DEFAULT_EFFECTS "identity,exclusion,navigationtest," \
        //    "agingtv,videoflip,vertigotv,gaussianblur,shagadelictv,edgetv"
        //const string DEFAULT_EFFECTS = "identity,exclusion,agingtv,videoflip,vertigotv,gaussianblur,shagadelictv,edgetv";
        const string DEFAULT_EFFECTS = "identity,exclusion,agingtv,videoflip,vertigotv,gaussianblur,shagadelictv,edgetv";

        //static GstPad *blockpad;
        //static GstElement *conv_before;
        //static GstElement *conv_after;
        //static GstElement *cur_effect;
        //static GstElement *pipeline;

        static Pad blockpad;
        static Element conv_before;
        static Element conv_after;
        static Element cur_effect;
        static Pipeline pipeline;

        private static Element mixer;

        private static Element filter;
        private static Element src;

        private static int xCord = 0;
        private static int yCord = 0;

        //static GQueue effects = G_QUEUE_INIT;
        static Queue<Element> effects = new Queue<Element>();
        static int currentPattern = 0;

        private static List<SourcePipeline> sources = new();

        private static PadTemplate mixerPadTemplate;


        //static GstPadProbeReturn event_probe_cb (GstPad * pad, GstPadProbeInfo * info, gpointer user_data)
        static PadProbeReturn event_probe_cb(ulong pid, Pad pad, PadProbeInfo info, MainLoop loop)
        {
            //  GMainLoop *loop = user_data;
            //  GstElement *next;

            //  if (GST_EVENT_TYPE (GST_PAD_PROBE_INFO_DATA (info)) != GST_EVENT_EOS)
            //    return GST_PAD_PROBE_OK;
            if (info.Event.Type != EventType.Eos)
                return PadProbeReturn.Ok;

            //  gst_pad_remove_probe (pad, GST_PAD_PROBE_INFO_ID (info));
            pad.RemoveProbe(pid);

            //  /* take next effect from the queue */
            //  next = g_queue_pop_head (&effects);
            //  if (next == NULL) {
            //    GST_DEBUG_OBJECT (pad, "no more effects");
            //    g_main_loop_quit (loop);
            //    return GST_PAD_PROBE_DROP;
            //  }
            if (effects.Count <= 0)
            {
                Console.WriteLine("no more effects");
                loop.Quit();
            }
            var next = effects.Dequeue();

            //  g_print ("Switching from '%s' to '%s'..\n", GST_OBJECT_NAME (cur_effect),
            //      GST_OBJECT_NAME (next));
            Console.WriteLine("Switching from '{0}' to '{1}'..\n", cur_effect.Name, next.Name);

            //  gst_element_set_state (cur_effect, GST_STATE_NULL);
            cur_effect.SetState(State.Null);

            //  /* remove unlinks automatically */
            //  GST_DEBUG_OBJECT (pipeline, "removing %" GST_PTR_FORMAT, cur_effect);
            //  gst_bin_remove (GST_BIN (pipeline), cur_effect);
            Console.WriteLine(pipeline.Name + " removing %" + cur_effect.Name);
            pipeline.Remove(cur_effect);

            //  /* push current effect back into the queue */
            //  g_queue_push_tail (&effects, g_steal_pointer (&cur_effect));
            effects.Enqueue(cur_effect);

            //  /* add, link and start the new effect */
            //  GST_DEBUG_OBJECT (pipeline, " adding   %" GST_PTR_FORMAT, next);
            //  gst_bin_add (GST_BIN (pipeline), next);
            Console.WriteLine(pipeline.Name + "adding   %" + next.Name);
            pipeline.Add(next);

            //  GST_DEBUG_OBJECT (pipeline, "linking..");
            //  gst_element_link_many (conv_before, next, conv_after, NULL);
            Console.WriteLine(pipeline.Name + " linking.. " + string.Join(", ", conv_before.Name, next.Name, conv_after.Name));
            if (!gst_element_link_many(conv_before, next, conv_after))
            {
                Console.WriteLine("Failed to link!!!");
            }

            //  gst_element_set_state (next, GST_STATE_PLAYING);
            next.SetState(State.Playing);

            //  cur_effect = next;
            //  GST_DEBUG_OBJECT (pipeline, "done");
            cur_effect = next;
            Console.WriteLine(pipeline.Name + " done");

            //  return GST_PAD_PROBE_DROP;
            return PadProbeReturn.Drop;
        }


        //static GstPadProbeReturn pad_probe_cb (GstPad * pad, GstPadProbeInfo * info, gpointer user_data)
        static PadProbeReturn pad_probe_cb(ulong pid, Pad pad, PadProbeInfo info, MainLoop user_data)
        {
            //  GstPad *srcpad, *sinkpad;

            //  GST_DEBUG_OBJECT (pad, "pad is blocked now");

            //  /* remove the probe first */
            //  gst_pad_remove_probe (pad, GST_PAD_PROBE_INFO_ID (info));
            pad.RemoveProbe(pid);

            //  /* install new probe for EOS */
            //  srcpad = gst_element_get_static_pad (cur_effect, "src");
            //  gst_pad_add_probe (srcpad, GST_PAD_PROBE_TYPE_BLOCK |
            //      GST_PAD_PROBE_TYPE_EVENT_DOWNSTREAM, event_probe_cb, user_data, NULL);
            //  gst_object_unref (srcpad);
            var srcpad = mixer.GetStaticPad("src");

            ulong pid2 = 0;
            pid2 = srcpad.AddProbe(PadProbeType.Block | PadProbeType.EventDownstream, (pad2, info2) => my_timeout_cb(pid2, pad2, info2, user_data));


            //  /* push EOS into the element, the probe will be fired when the
            //   * EOS leaves the effect and it has thus drained all of its data */
            //  sinkpad = gst_element_get_static_pad (cur_effect, "sink");
            //  gst_pad_send_event (sinkpad, gst_event_new_eos ());
            //  gst_object_unref (sinkpad);
            var sinkpad = mixer.GetRequestPad("");
            sinkpad.SendEvent(Event.NewEos());

            //  return GST_PAD_PROBE_OK;
            return PadProbeReturn.Ok;
        }

        static bool timeout_cb(MainLoop user_data)
        {
            xCord += 320;
            if (xCord >= 640)
            {
                xCord = 0;
                yCord += 240;
            }

            if (yCord >= 480)
            {
                return true;
            }
            
            var src2 = newSource(++currentPattern);
            sources.Add(src2);
            pipeline.Add(src2.source, src2.filter, src2.conv);
            gst_element_link_many(src2.source, src2.filter, src2.conv);
            
            var newPad = mixer.RequestPad(mixerPadTemplate);
            var lastBoxPad = src2.conv.GetStaticPad("src");
            var result = lastBoxPad.Link(newPad);
            Console.WriteLine(result);
            
            newPad.SetProperty("xpos", new Value(xCord));
            newPad.SetProperty("ypos", new Value(yCord));
            newPad.SetProperty("width", new Value(320));
            newPad.SetProperty("height", new Value(240));

            src2.source.SetState(State.Playing);
            src2.filter.SetState(State.Playing);
            src2.conv.SetState(State.Playing);
            
            // foreach (var source in sources)
            // {
            //     ulong pid = 0;
            //     source.conv.GetStaticPad("src").AddProbe(PadProbeType.BlockDownstream, (pad, info) => pad_probe_cb(pid, pad, info, user_data));
            // }
            return true;

            // gst_pad_add_probe(blockpad, GST_PAD_PROBE_TYPE_BLOCK_DOWNSTREAM, pad_probe_cb, user_data, NULL);
            // ulong pid = 0;
            // pid = blockpad.AddProbe(PadProbeType.BlockDownstream, (pad, info) => pad_probe_cb(pid, pad, info, user_data));
            // return true;
        }

        private static bool temp_cb(MainLoop user_data)
        {
            // var srcpad = filter.GetStaticPad("sink");
            // ulong pid2 = 0;
            // pid2 = srcpad.AddProbe(PadProbeType.Block | PadProbeType.EventDownstream, (pad2, info2) => my_timeout_cb(pid2, pad2, info2, user_data));
            return true;
        }
        
        static PadProbeReturn my_timeout_cb(ulong pid, Pad pad, PadProbeInfo info, MainLoop user_data)
        {
            // if (info.Event.Type != EventType.Eos)
            //     return PadProbeReturn.Ok;
            
            pad.RemoveProbe(pid);
            
            if (++currentPattern == 26)
            {
                currentPattern = 0;
            }
            
            Console.Write($"Change pattern to {currentPattern}");

            src.SetState(State.Null);
            
            src.SetProperty("pattern", new Value(currentPattern));

            src.SetState(State.Playing);

            return PadProbeReturn.Drop;
        }

        static bool bus_cb(Bus bus, Message msg, MainLoop loop)
        {
            switch (msg.Type)
            {
                case MessageType.Error:
                    msg.ParseError(out var err, out var debug_info);
                    Console.WriteLine("Error received from element {0}: {1}\n", err.Source, err.Message);
                    Console.WriteLine("Debugging information: {0}\n", debug_info ?? "none");
                    loop.Quit();
                    break;

                default:
                    break;
            }
            return true;
        }


        static void Main(string[] args)
        {
            Environment.SetEnvironmentVariable("PATH", @"C:\gstreamer\1.0\msvc_x86_64\bin;" + Environment.GetEnvironmentVariable("PATH"));

            Gst.Application.Init(ref args);

            string[] effect_names = DEFAULT_EFFECTS.Split(',');

            foreach (var e in effect_names)
            {
                Element el = ElementFactory.Make(e);
                if (el != null)
                {
                    // Console.WriteLine("Adding effect '{0}'\n", e);
                    effects.Enqueue(el);
                }
            }

            pipeline = new Pipeline("pipeline");

            var source = newSource(0);
            sources.Add(source);
            pipeline.Add(source.source, source.filter, source.conv);
            gst_element_link_many(source.source, source.filter, source.conv);

            mixer = ElementFactory.Make("compositor");
            
            var filter = ElementFactory.Make("capsfilter");
            Util.SetObjectArg(filter, "caps", "video/x-raw, width=640, height=480");
            
            conv_after = ElementFactory.Make("videoconvert");

            
            var q2 = ElementFactory.Make("queue");
            var sink = ElementFactory.Make("autovideosink");
            
            pipeline.Add(mixer, filter, conv_after, q2, sink);
            gst_element_link_many(mixer, filter, conv_after, q2, sink);

            mixerPadTemplate = mixer.PadTemplateList.First(it => it.Name == "sink_%u");
            var newPad = mixer.RequestPad(mixerPadTemplate);
            var lastBoxPad = source.conv.GetStaticPad("src");
            var result = lastBoxPad.Link(newPad);
            Console.WriteLine(result);
            
            newPad.SetProperty("xpos", new Value(xCord));
            newPad.SetProperty("ypos", new Value(yCord));
            newPad.SetProperty("width", new Value(320));
            newPad.SetProperty("height", new Value(240));
            
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.WriteLine("Error starting pipeline");
                return;
            }

            var loop = new MainLoop();

            pipeline.Bus.AddWatch((bus, msg) => bus_cb(bus, msg, loop));

            Timeout.AddSeconds(2, () => timeout_cb(loop));

            loop.Run();
            
            pipeline.SetState(State.Null);
            
            pipeline.Bus.RemoveWatch();

            effects.Clear();

            //  return 0;

            Console.ReadKey();
        }

        public static IEnumerable<R> SelectSlidingPairs<T, R>(this IEnumerable<T> seq, Func<T, T, R> action)
        {
            var it = seq.GetEnumerator();
            if (it.MoveNext())
            {
                var prev = it.Current;
                while (it.MoveNext())
                {
                    var curr = it.Current;
                    yield return action(prev, curr);
                    prev = curr;
                }
            }
        }

        public static bool gst_element_link_many(params Element[] el)
        {
            return el.SelectSlidingPairs((a, b) => a.Link(b)).All(x => x);
        }

        public static SourcePipeline newSource(int pattern)
        {
            var source = ElementFactory.Make("videotestsrc");
            source.SetProperty("is-live", new GLib.Value(true));
            source.SetProperty("pattern", new Value(pattern));
            var filter = ElementFactory.Make("capsfilter");
            Util.SetObjectArg(filter, "caps", "video/x-raw, width=320, height=240, framerate=20/1, format={ I420, YV12, YUY2, UYVY, AYUV, Y41B, Y42B, YVYU, Y444, v210, v216, NV12, NV21, UYVP, A420, YUV9, YVU9, IYU1 }");
            // var q1 = ElementFactory.Make("queue");
            var conv_before = ElementFactory.Make("videoconvert");
            // var box = ElementFactory.Make("videobox");

            var sourcePipeline = new SourcePipeline()
            {
                source = source,
                filter = filter,
                // queue = q1,
                conv = conv_before,
                // box = box
            };

            return sourcePipeline;
        }
    }

    class SourcePipeline
    {
        public Element source;
        public Element filter;
        // public Element queue;
        public Element conv;
        // public Element box;
    }
}
