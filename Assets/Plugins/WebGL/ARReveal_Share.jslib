// Native OS share, bypassing Zappar's own zappar-sharing.js overlay entirely
// (Save/Share/Close custom modal) - per direct request: "skip zappars ui and
// go native os". Called from ARShareController.ShareLastPhoto() with the raw
// JPEG bytes already captured on the C# side (own ReadPixels/EncodeToJPG,
// not Zappar's ZSaveNShare) - byte[] + explicit length is Unity's standard
// WebGL marshalling convention for passing an array into a jslib function
// (see Zappar's own zappar_sns_jpg_snapshot for the same pattern).
//
// navigator.share() IS the actual native OS share sheet - a real system UI,
// not something this project renders - so once it's invoked, "the phone does
// its thing" exactly as asked, with no custom overlay of ours or Zappar's in
// between. Falls back to a plain download (same graceful-degrade Zappar's
// own "Save" button used) if this browser/device has no file-sharing support
// at all, so tapping the button never silently does nothing.
mergeInto(LibraryManager.library, {
    ARReveal_ShareImage: function (dataPtr, length) {
        var bytes = new Uint8Array(length);
        for (var i = 0; i < length; i++) bytes[i] = HEAPU8[dataPtr + i];
        var blob = new Blob([bytes], { type: 'image/jpeg' });
        var file = new File([blob], 'ar-photo.jpg', { type: 'image/jpeg' });

        if (navigator.canShare && navigator.canShare({ files: [file] })) {
            navigator.share({ files: [file] }).catch(function (e) {
                console.log('[ARReveal] navigator.share failed: ' + e.message);
            });
            return;
        }

        // No native file-sharing support here - fall back to a direct
        // download rather than leaving the tap with no effect at all.
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = 'ar-photo.jpg';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    }
});
