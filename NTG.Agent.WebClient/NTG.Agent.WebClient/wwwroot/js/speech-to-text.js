window.speechRecognizer = {
    recognition: null,

    startRecognition: function (dotNetHelper, language) {
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

        if (!SpeechRecognition) {
            alert("Your browser does not support Speech Recognition.");
            return;
        }

        if (this.recognition) {
            this.recognition.stop();
            this.recognition = null;
        }

        let recognition = new SpeechRecognition();
        recognition.lang = language || "en-US";
        recognition.interimResults = false;
        recognition.maxAlternatives = 1;

        recognition.onresult = function (event) {
            let transcript = event.results[0][0].transcript;
            dotNetHelper.invokeMethodAsync("ReceiveTranscription", transcript);
        };

        recognition.onerror = function (event) {
            console.error("Speech recognition error", event.error);
        };

        recognition.onend = function () {
            dotNetHelper.invokeMethodAsync("OnSpeechEnded");
            window.speechRecognizer.recognition = null;
        };

        recognition.start();
        this.recognition = recognition;
    },

    stopRecognition: function () {
        if (this.recognition) {
            this.recognition.stop();
            this.recognition = null;
        }
    }
};
