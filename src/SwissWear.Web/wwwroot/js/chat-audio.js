window.chatAudio = {
    _recorder: null,
    _chunks: [],
    _dotNetRef: null,
    _stream: null,
    _players: {},

    start: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        this._chunks = [];

        var self = this;
        return navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
            self._stream = stream;
            self._recorder = new MediaRecorder(stream, { audioBitsPerSecond: 16000 });

            self._recorder.addEventListener('dataavailable', function (e) {
                if (e.data && e.data.size > 0) {
                    self._chunks.push(e.data);
                }
            });

            self._recorder.addEventListener('stop', function () {
                if (self._stream) {
                    self._stream.getTracks().forEach(function (t) { t.stop(); });
                    self._stream = null;
                }

                if (self._chunks.length === 0) return;

                var mimeType = self._recorder.mimeType || 'audio/webm';
                var blob = new Blob(self._chunks, { type: mimeType });
                self._chunks = [];

                var reader = new FileReader();
                reader.onloadend = function () {
                    var base64 = reader.result.split(',')[1];
                    self._sendChunked(base64);
                };
                reader.readAsDataURL(blob);
            });

            self._recorder.start(250);
            return true;
        }).catch(function () {
            return false;
        });
    },

    stop: function () {
        if (this._recorder && this._recorder.state === 'recording') {
            this._recorder.stop();
        }
    },

    _sendChunked: function (base64) {
        var chunkSize = 20000;
        var totalChunks = Math.ceil(base64.length / chunkSize);
        var ref = this._dotNetRef;
        if (!ref) return;

        var sendNext = function (i) {
            if (i >= totalChunks) {
                ref.invokeMethodAsync('OnAudioChunkReceived', '', i, totalChunks, true);
                return;
            }
            var chunk = base64.substring(i * chunkSize, (i + 1) * chunkSize);
            ref.invokeMethodAsync('OnAudioChunkReceived', chunk, i, totalChunks, false)
                .then(function () { sendNext(i + 1); })
                .catch(function (err) { console.error('[chatAudio] chunk send failed:', err); });
        };
        sendNext(0);
    },

    // ── Custom audio player ──
    initPlayer: function (playerId, src) {
        var audio = new Audio(src);
        var self = this;
        this._players[playerId] = { audio: audio, playing: false };

        audio.addEventListener('loadedmetadata', function () {
            var dur = isFinite(audio.duration) ? audio.duration : 0;
            var el = document.getElementById(playerId);
            if (el) el.querySelector('.ap-duration').textContent = self._formatTime(dur);
        });

        audio.addEventListener('timeupdate', function () {
            var el = document.getElementById(playerId);
            if (!el) return;
            var pct = audio.duration ? (audio.currentTime / audio.duration) * 100 : 0;
            el.querySelector('.ap-progress-fill').style.width = pct + '%';
            el.querySelector('.ap-current').textContent = self._formatTime(audio.currentTime);
        });

        audio.addEventListener('ended', function () {
            self._players[playerId].playing = false;
            var el = document.getElementById(playerId);
            if (el) {
                el.querySelector('.ap-btn').textContent = '▶';
                el.querySelector('.ap-progress-fill').style.width = '0%';
                el.querySelector('.ap-current').textContent = '0:00';
            }
        });
    },

    togglePlay: function (playerId) {
        var p = this._players[playerId];
        if (!p) return;

        if (p.playing) {
            p.audio.pause();
            p.playing = false;
            var el = document.getElementById(playerId);
            if (el) el.querySelector('.ap-btn').textContent = '▶';
        } else {
            // Pause all other players
            for (var id in this._players) {
                if (id !== playerId && this._players[id].playing) {
                    this._players[id].audio.pause();
                    this._players[id].playing = false;
                    var otherEl = document.getElementById(id);
                    if (otherEl) otherEl.querySelector('.ap-btn').textContent = '▶';
                }
            }
            p.audio.play();
            p.playing = true;
            var el = document.getElementById(playerId);
            if (el) el.querySelector('.ap-btn').textContent = '⏸';
        }
    },

    seekPlayer: function (playerId, event) {
        var p = this._players[playerId];
        if (!p || !p.audio.duration) return;
        var bar = event.currentTarget;
        var rect = bar.getBoundingClientRect();
        var pct = (event.clientX - rect.left) / rect.width;
        p.audio.currentTime = pct * p.audio.duration;
    },

    _formatTime: function (s) {
        if (!isFinite(s)) return '0:00';
        var m = Math.floor(s / 60);
        var sec = Math.floor(s % 60);
        return m + ':' + (sec < 10 ? '0' : '') + sec;
    }
};
