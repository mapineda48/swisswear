export function initAudioPlayers() {
    document.querySelectorAll('.chat-audio-player').forEach(function (el) {
        if (el.dataset.init) return;
        el.dataset.init = '1';
        var id = el.id;
        var audioEl = document.getElementById(id + '-audio');
        if (audioEl) chatAudio.initPlayer(id, audioEl.querySelector('source').src);
    });
}

export function ensureFileInputMultiple() {
    var fi = document.getElementById('chat-file-input');
    if (fi && !fi.hasAttribute('multiple')) fi.setAttribute('multiple', '');
}

export function scrollToBottom(elementId) {
    var el = document.getElementById(elementId);
    if (el) el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
}

export function triggerFileInput(inputId) {
    var el = document.getElementById(inputId);
    if (el) el.click();
}

export function destroyAudioPlayers(playerIds) {
    if (!window.chatAudio || !window.chatAudio._players) return;
    for (var i = 0; i < playerIds.length; i++) {
        var id = playerIds[i];
        var player = window.chatAudio._players[id];
        if (player) {
            if (player.playing) player.audio.pause();
            player.audio.src = '';
            delete window.chatAudio._players[id];
        }
    }
}
