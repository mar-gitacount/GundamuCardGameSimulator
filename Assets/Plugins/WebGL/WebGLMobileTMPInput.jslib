mergeInto(LibraryManager.library, {
  WebGLMobileTMPInput_Show: function (x, y, width, height, textPtr, isPassword, fontSize, unityScreenHeight) {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
      return;
    }

    var text = UTF8ToString(textPtr);
    var input = window.__unityMobileTmpInput;

    if (!input) {
      input = document.createElement('input');
      input.id = 'unity-mobile-tmp-input';
      input.autocomplete = 'off';
      input.autocapitalize = 'off';
      input.autocorrect = 'off';
      input.spellcheck = false;
      input.style.position = 'fixed';
      input.style.zIndex = '2147483647';
      input.style.margin = '0';
      input.style.padding = '0 12px';
      input.style.border = '1px solid #4a90e2';
      input.style.borderRadius = '6px';
      input.style.outline = 'none';
      input.style.boxSizing = 'border-box';
      input.style.background = 'rgba(20, 26, 36, 0.98)';
      input.style.color = '#ffffff';
      input.style.caretColor = '#ffffff';
      input.style.webkitAppearance = 'none';
      input.style.appearance = 'none';
      input.style.touchAction = 'manipulation';
      input.style.opacity = '1';
      input.style.pointerEvents = 'auto';
      document.body.appendChild(input);

      input.addEventListener('input', function () {
        var inst = window.unityInstance || window.gameInstance;
        if (inst && inst.SendMessage) {
          inst.SendMessage('WebGLMobileInputReceiver', 'OnHtmlInput', input.value);
        }
      });

      input.addEventListener('blur', function () {
        input.style.display = 'none';
        var inst = window.unityInstance || window.gameInstance;
        if (inst && inst.SendMessage) {
          inst.SendMessage('WebGLMobileInputReceiver', 'OnHtmlBlur', '');
        }
      });

      window.__unityMobileTmpInput = input;
    }

    var isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent) ||
      (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

    input.type = isPassword ? 'password' : 'text';
    input.inputMode = 'text';
    input.enterKeyHint = isPassword ? 'done' : 'next';
    input.value = text || '';
    input.style.fontSize = Math.max(fontSize, 16) + 'px';
    input.style.display = 'block';

    if (isIOS) {
      input.style.left = '5vw';
      input.style.top = '38vh';
      input.style.width = '90vw';
      input.style.height = '44px';
    } else {
      var canvas = document.getElementById('unity-canvas') || document.querySelector('canvas');
      var cssX = x;
      var cssY = y;
      var cssW = Math.max(width, 48);
      var cssH = Math.max(height, 44);

      if (canvas) {
        var rect = canvas.getBoundingClientRect();
        if (canvas.width > 0 && canvas.height > 0) {
          var scaleX = rect.width / canvas.width;
          var scaleY = rect.height / canvas.height;
          cssX = rect.left + x * scaleX;
          cssY = rect.top + (unityScreenHeight - (y + height)) * scaleY;
          cssW = Math.max(width * scaleX, 48);
          cssH = Math.max(height * scaleY, 44);
        }
      }

      input.style.left = cssX + 'px';
      input.style.top = cssY + 'px';
      input.style.width = cssW + 'px';
      input.style.height = cssH + 'px';
    }

    input.focus({ preventScroll: false });
    input.click();
    if (input.setSelectionRange) {
      var len = input.value ? input.value.length : 0;
      input.setSelectionRange(len, len);
    }
  },

  WebGLMobileTMPInput_Hide: function () {
    if (typeof window === 'undefined') {
      return;
    }

    var input = window.__unityMobileTmpInput;
    if (input) {
      input.blur();
      input.style.display = 'none';
    }
  }
});
