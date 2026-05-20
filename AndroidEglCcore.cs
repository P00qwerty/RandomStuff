
using Android.Opengl;


public class Offscreen
{
    private EglCore mEglCore;
    private EGLSurface mEGLSurface = EGL14.EglNoSurface;
    private ByteBuffer pixelBuf;

    public void Start()
    {
        pixelBuf = ByteBuffer.AllocateDirect(bufferSize);
        pixelBuf.Order(ByteOrder.LittleEndian);

        mEglCore = new EglCore(null, 0);
        mEGLSurface = EGL14.EglNoSurface;

        if (mEGLSurface == EGL14.EglNoSurface)
        {
            mEGLSurface = mEglCore.createOffscreenSurface(bufferWidth, bufferHeight);

            mEglCore.makeCurrent(mEGLSurface);

            //here you can start using GLES10 functions
            GLES10.GlViewport(0, 0, bufferWidth, bufferHeight);
        }


        //RELEASE

        if (mEglCore != null)
        {
            if (mEGLSurface != EGL14.EglNoSurface)
                mEglCore.releaseSurface(mEGLSurface);
            mEGLSurface = EGL14.EglNoSurface;
            mEglCore.Release();
        }
    }
}

public class EglCore
{
    /**
     * Constructor flag: surface must be recordable.  This discourages EGL from using a
     * pixel format that cannot be converted efficiently to something usable by the video
     * encoder.
     */
    public static int FLAG_RECORDABLE = 0x01;

    // Android-specific extension.
    private static int EGL_RECORDABLE_ANDROID = 0x3142;

    public EGLDisplay mEGLDisplay = EGL14.EglNoDisplay;
    private EGLContext mEGLContext = EGL14.EglNoContext;
    private EGLConfig mEGLConfig = null;

    /**
     * Prepares EGL display and context.
     * <p>
     * @param sharedContext The context to share, or null if sharing is not desired.
     * @param flags Configuration bit flags, e.g. FLAG_RECORDABLE.
     */
    public EglCore(EGLContext sharedContext, int flags)
    {
        if (mEGLDisplay != EGL14.EglNoDisplay)
        {
            throw new Exception("EGL already set up");
        }

        if (sharedContext == null)
        {
            sharedContext = EGL14.EglNoContext;
        }

        mEGLDisplay = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
        if (mEGLDisplay == EGL14.EglNoDisplay)
        {
            throw new Exception("unable to get EGL14 display");
        }
        int[] version = new int[2];
        if (!EGL14.EglInitialize(mEGLDisplay, version, 0, version, 1))
        {
            mEGLDisplay = null;
            throw new Exception("unable to initialize EGL14");
        }

        if (mEGLContext == EGL14.EglNoContext)
        {  // GLES 2 only, or GLES 3 attempt failed
            EGLConfig config = getConfig(flags, 2);
            if (config == null)
            {
                throw new Exception("Unable to find a suitable EGLConfig");
            }
            int[] attrib2_list = {
                    EGL14.EglContextClientVersion, 1,
                    EGL14.EglNone
            };
            EGLContext context = EGL14.EglCreateContext(mEGLDisplay, config, sharedContext,
                    attrib2_list, 0);
            checkEglError("eglCreateContext");
            mEGLConfig = config;
            mEGLContext = context;
        }

        // Confirm with query.
        int[] values = new int[1];
        EGL14.EglQueryContext(mEGLDisplay, mEGLContext, EGL14.EglContextClientVersion,
                values, 0);
    }

    /**
     * Finds a suitable EGLConfig.
     *
     * @param flags Bit flags from constructor.
     * @param version Must be 2 or 3.
     */
    private EGLConfig getConfig(int flags, int version)
    {
        int renderableType = EGL14.EglOpenglEs2Bit;
        if (version >= 3)
        {
            renderableType |= EGLExt.EglOpenglEs3BitKhr;
        }

        // The actual surface is generally RGBA or RGBX, so situationally omitting alpha
        // doesn't really help.  It can also lead to a huge performance hit on glReadPixels()
        // when reading into a GL_RGBA buffer.
        int[] attribList = {
                EGL14.EglRedSize, 8,
                EGL14.EglGreenSize, 8,
                EGL14.EglBlueSize, 8,
                EGL14.EglAlphaSize, 8,
                //EGL14.EGL_DEPTH_SIZE, 16,
                //EGL14.EGL_STENCIL_SIZE, 8,
                EGL14.EglRenderableType, renderableType,
                EGL14.EglNone, 0,      // placeholder for recordable [@-3]
                EGL14.EglNone
            };

        if ((flags & FLAG_RECORDABLE) != 0)
        {
            attribList[attribList.Length - 3] = EGL_RECORDABLE_ANDROID;
            attribList[attribList.Length - 2] = 1;
        }
        EGLConfig[] configs = new EGLConfig[1];
        int[] numConfigs = new int[1];
        if (!EGL14.EglChooseConfig(mEGLDisplay, attribList, 0, configs, 0, configs.Length,
                numConfigs, 0))
        {
            return null;
        }
        return configs[0];
    }

    /**
     * Discards all resources held by this class, notably the EGL context.  This must be
     * called from the thread where the context was created.
     * <p>
     * On completion, no context will be current.
     */
    public void Release()
    {
        if (mEGLDisplay != EGL14.EglNoDisplay)
        {
            // Android is unusual in that it uses a reference-counted EGLDisplay.  So for
            // every eglInitialize() we need an eglTerminate().
            EGL14.EglMakeCurrent(mEGLDisplay, EGL14.EglNoSurface, EGL14.EglNoSurface,
                    EGL14.EglNoContext);
            EGL14.EglDestroyContext(mEGLDisplay, mEGLContext);
            EGL14.EglReleaseThread();
            EGL14.EglTerminate(mEGLDisplay);
        }

        mEGLDisplay = EGL14.EglNoDisplay;
        mEGLContext = EGL14.EglNoContext;
        mEGLConfig = null;
    }

    /**
     * Destroys the specified surface.  Note the EGLSurface won't actually be destroyed if it's
     * still current in a context.
     */
    public void releaseSurface(EGLSurface eglSurface)
    {
        EGL14.EglDestroySurface(mEGLDisplay, eglSurface);
    }

    /**
     * Creates an EGL surface associated with a Surface.
     * <p>
     * If this is destined for MediaCodec, the EGLConfig should have the "recordable" attribute.
     */
    public EGLSurface createWindowSurface(Java.Lang.Object surface)
    {
        if (!(surface is Surface) && !(surface is SurfaceTexture))
        {
            throw new Exception("invalid surface: " + surface);
        }

        // Create a window surface, and attach it to the Surface we received.
        int[] surfaceAttribs = { EGL14.EglNone };
        EGLSurface eglSurface = EGL14.EglCreateWindowSurface(mEGLDisplay, mEGLConfig, surface, surfaceAttribs, 0);
        checkEglError("eglCreateWindowSurface");
        if (eglSurface == null)
        {
            throw new Exception("surface was null");
        }
        return eglSurface;
    }

    /**
     * Creates an EGL surface associated with an offscreen buffer.
     */
    public EGLSurface createOffscreenSurface(int width, int height)
    {
        int[] surfaceAttribs = { EGL14.EglWidth, width, EGL14.EglHeight, height, EGL14.EglNone };
        EGLSurface eglSurface = EGL14.EglCreatePbufferSurface(mEGLDisplay, mEGLConfig, surfaceAttribs, 0);
        checkEglError("eglCreatePbufferSurface");
        if (eglSurface == null)
        {
            throw new Exception("surface was null");
        }
        return eglSurface;
    }

    /**
     * Makes our EGL context current, using the supplied surface for both "draw" and "read".
     */
    public void makeCurrent(EGLSurface eglSurface)
    {
        if (mEGLDisplay == EGL14.EglNoDisplay)
        {
        }

        if (!EGL14.EglMakeCurrent(mEGLDisplay, eglSurface, eglSurface, mEGLContext))
        {
            throw new Exception("eglMakeCurrent failed");
        }
    }

    /**
     * Makes our EGL context current, using the supplied "draw" and "read" surfaces.
     */
    public void makeCurrent(EGLSurface drawSurface, EGLSurface readSurface)
    {
        if (mEGLDisplay == EGL14.EglNoDisplay)
        {
        }
        if (!EGL14.EglMakeCurrent(mEGLDisplay, drawSurface, readSurface, mEGLContext))
        {
            throw new Exception("eglMakeCurrent(draw,read) failed");
        }
    }

    /**
     * Makes no context current.
     */
    public void makeNothingCurrent()
    {
        if (!EGL14.EglMakeCurrent(mEGLDisplay, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext))
        {
            throw new Exception("eglMakeCurrent failed");
        }
    }

    /**
     * Calls eglSwapBuffers.  Use this to "publish" the current frame.
     *
     * @return false on failure
     */
    public bool swapBuffers(EGLSurface eglSurface)
    {
        return EGL14.EglSwapBuffers(mEGLDisplay, eglSurface);
    }

    /**
     * Checks for EGL errors.  Throws an exception if an error has been raised.
     */
    private void checkEglError(string msg)
    {
        int error;
        if ((error = EGL14.EglGetError()) != EGL14.EglSuccess)
        {
            throw new Exception(msg + ": EGL error: 0x" + error.ToString());
        }
    }
}