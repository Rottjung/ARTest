// Same-tab page reload for ARShareController.Restart().
//
// Application.OpenURL(Application.absoluteURL) is NOT reliable for this:
// depending on the WebGL template it can call window.open(url, "_blank"),
// which opens a SECOND tab and leaves the original (with its live camera
// feed) still running behind it - exactly the opposite of "restart".
//
// window.location.reload() is the correct primitive here: it reloads the
// same tab in place, at the same origin. Browser camera/microphone
// permissions are granted per-ORIGIN, not per page-load, so they are NOT
// re-prompted after this - the user only sees the permission prompt again
// if they reload from a different origin/URL, use a private/incognito
// window, or have explicitly reset the site's permissions themselves.
mergeInto(LibraryManager.library, {
    ARReveal_ReloadPage: function () {
        window.location.reload();
    }
});
