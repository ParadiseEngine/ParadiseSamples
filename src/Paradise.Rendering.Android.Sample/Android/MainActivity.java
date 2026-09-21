package org.paradiseengine.androidsmoke;

import org.libsdl.app.SDLActivity;

/** SDL owns JNI, input and the app thread; all engine code lives in the NativeAOT library. */
public final class MainActivity extends SDLActivity {
    @Override
    protected String[] getLibraries() {
        // SDL resolves SDL_main from the final library in this list.
        return new String[] { "SDL3", "webgpu_dawn", "paradise_android" };
    }
}
