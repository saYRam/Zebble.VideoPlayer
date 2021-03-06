namespace Zebble
{
    using Android.Graphics;
    using Android.Media;
    using Android.Runtime;
    using Android.Views;
    using Android.Widget;
    using System;
    using Zebble.Device;
    using static Zebble.VideoPlayer;

    class AndroidVideo : RelativeLayout, ISurfaceHolderCallback, MediaPlayer.IOnPreparedListener
    {
        VideoPlayer View;

        SurfaceView VideoSurface;
        MediaPlayer VideoPlayer;
        Preparedhandler Prepared = new Preparedhandler();
        Preparedhandler VideoSurfaceCreate = new Preparedhandler();
        bool IsSurfaceCreated, IsVideoBuffered;

        [Preserve]
        public AndroidVideo(IntPtr handle, JniHandleOwnership transfer) : base(UIRuntime.CurrentActivity) { }

        public AndroidVideo(VideoPlayer view) : base(UIRuntime.CurrentActivity)
        {
            View = view;
            CreateSurfceView();
            CreateVideoPlayer();

            View.Buffered.HandleOn(Thread.UI, () => SafeInvoke(() => { VideoPlayer.PrepareAsync(); IsVideoBuffered = true; }));
            View.PathChanged.HandleOn(Thread.UI, () => SafeInvoke(() => { if (View.AutoPlay) LoadVideo(); }));
            View.Started.HandleOn(Thread.UI, () => SafeInvoke(OnVideoStart));
            View.Paused.HandleOn(Thread.UI, () => SafeInvoke(() => Prepared.Raise(VideoState.Pause)));
            View.Resumed.HandleOn(Thread.UI, () => SafeInvoke(() => Prepared.Raise(VideoState.Play)));
            View.Stopped.HandleOn(Thread.UI, () => SafeInvoke(() => Prepared.Raise(VideoState.Stop)));
            View.SoughtBeginning.HandleOn(Thread.UI, () => SafeInvoke(() => Prepared.Raise(VideoState.SeekToBegining)));
            Prepared.Handle(HandleStateCommand);
        }

        void CreateSurfceView()
        {
            VideoSurface = new SurfaceView(UIRuntime.CurrentActivity);
            VideoSurface.Holder.AddCallback(this);
            VideoSurface.LayoutParameters = CreateLayout();
            AddView(VideoSurface);
        }

        void CreateVideoPlayer()
        {
            VideoPlayer = new MediaPlayer();
            VideoPlayer.SetOnPreparedListener(this);
            VideoPlayer.Completion += OnCompletion;
            VideoPlayer.VideoSizeChanged += OnVideoSizeChanged;
        }

        LayoutParams CreateLayout()
        {
            var result = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            result.AddRule(LayoutRules.AlignParentTop);
            result.AddRule(LayoutRules.AlignParentBottom);
            result.AddRule(LayoutRules.AlignParentLeft);
            result.AddRule(LayoutRules.AlignParentRight);
            return result;
        }

        void ISurfaceHolderCallback.SurfaceChanged(ISurfaceHolder holder, [GeneratedEnum] Format format, int width, int height) { }

        void ISurfaceHolderCallback.SurfaceCreated(ISurfaceHolder holder)
        {
            IsSurfaceCreated = holder.Surface?.IsValid == true;

            VideoPlayer.SetDisplay(VideoSurface.Holder);

            if (IsSurfaceCreated) VideoSurfaceCreate.Handle(state => LoadVideo());
            if (View.AutoPlay && IsSurfaceCreated) LoadVideo();
        }

        void ISurfaceHolderCallback.SurfaceDestroyed(ISurfaceHolder holder)
        {
            View.IsReady = false;
            IsSurfaceCreated = false;
            holder?.Surface?.Release();
        }

        void HandleStateCommand(VideoState result)
        {
            switch (result)
            {
                case VideoState.Play:
                    LoadVideo();
                    break;
                case VideoState.Pause:
                    if (VideoPlayer?.IsPlaying == true) VideoPlayer.Pause();
                    break;
                case VideoState.Stop:
                    VideoPlayer?.Stop();
                    VideoPlayer?.Reset();
                    break;
                case VideoState.SeekToBegining:
                    VideoPlayer?.Reset();
                    break;
                default: break;
            }
        }

        void MediaPlayer.IOnPreparedListener.OnPrepared(MediaPlayer mp)
        {
            if (IsDead(out var view)) return;

            try { mp.SetVideoScalingMode(VideoScalingMode.ScaleToFitWithCropping); }
            catch (Exception ex) { Log.Error(ex); }

            mp.Looping = View.Loop;
            View.IsReady = true;

            if (view.AutoBuffer) mp.Start();
        }

        void OnVideoStart()
        {
            if (IsDead(out var view)) return;

            if (IsVideoBuffered) VideoPlayer.Start();
            else Prepared.Raise(VideoState.Play).GetAwaiter();
        }

        void LoadVideo()
        {
            if (IsDead(out var view)) return;

            var source = view.Path;
            if (source.LacksValue()) return;

            if (!IsSurfaceCreated)
            {
                VideoSurfaceCreate.Raise(VideoState.Play);
                return;
            }

            if (IO.IsAbsolute(source)) source = "file://" + source;
            else if (!source.IsUrl()) source = "file://" + IO.AbsolutePath(source);
            try
            {
                VideoPlayer.Reset();
                VideoPlayer.SetDataSource(Renderer.Context, Android.Net.Uri.Parse(source));

                if (source.IsUrl() || View.AutoBuffer)
                    VideoPlayer.PrepareAsync();
            }
            catch (Java.Lang.Exception ex)
            {
                Log.Error("This error is raised without seemingly affecting anything! " + ex.Message);
            }
        }

        void SafeInvoke(Action action)
        {
            if (IsDead(out var view)) return;

            try { action(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        async void OnCompletion(object sender, EventArgs args)
        {
            if (IsDead(out var view)) return;
            await View.FinishedPlaying.RaiseOn(Thread.Pool);
        }

        void OnVideoSizeChanged(object sender, EventArgs args)
        {
            if (IsDead(out var view)) return;
            if (sender is MediaPlayer media)
            {
                if (view.VideoSize.Width == 0)
                {
                    view.VideoSize = new Size(media.VideoWidth, media.VideoHeight);
                    view.LoadCompleted?.RaiseOn(Thread.Pool);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && VideoPlayer != null)
            {
                VideoPlayer.Completion -= OnCompletion;
                VideoPlayer.VideoSizeChanged -= OnVideoSizeChanged;
                VideoPlayer.Release();
                VideoPlayer.Dispose();
                VideoPlayer = null;
                View = null;
            }

            base.Dispose(disposing);
        }

        [EscapeGCop("In this case an out parameter can improve the code.")]
        public bool IsDead(out VideoPlayer result)
        {
            result = View;
            if (result == null) return true;
            return result.IsDisposing;
        }
    }
}