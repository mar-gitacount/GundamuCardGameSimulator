mergeInto(LibraryManager.library, {
  WebGLMobileTMPInput_Show: function (x, y, width, height, textPtr, isPassword, fontSize) {
    var text = UTF8ToString(textPtr);
    var input = window.__unityMobileTmpInput;

    if (!input) {
      input = document.createElement('input');
      input.id = 'unity-mobile-tmp-input';
      input.autocomplete = 'off';
      input.autocapitalize = 'off';
      input.spellcheck = false;
      input.style.position = 'fixed';
      input.style.zIndex = '100000';
      input.style.margin = '0';
      input.style.padding = '0 8px';
      input.style.border = '1px solid #4a90e2';
      input.style.borderRadius = '4px';
      input.style.outline = 'none';
      input.style.boxSizing = 'border-box';
      input.style.background = 'rgba(20, 26, 36, 0.98)';
      input.style.color = '#ffffff';
      input.style.caretColor = '#ffffff';
      document.body.appendChild(input);

      input.addEventListener('input', function () {
        if (typeof unityInstance !== 'undefined' && unityInstance) {
          unityInstance.SendMessage('WebGLMobileInputReceiver', 'OnHtmlInput', input.value);
        } else if (typeof gameInstance !== 'undefined' && gameInstance) {
          gameInstance.SendMessage('WebGLMobileInputReceiver', 'OnHtmlInput', input.value);
        }
      });

      input.addEventListener('blur', function () {
        input.style.display = 'none';
        if (typeof unityInstance !== 'undefined' && unityInstance) {
          unityInstance.SendMessage('WebGLMobileInputReceiver', 'OnHtmlBlur', '');
        } else if (typeof gameInstance !== 'undefined' && gameInstance) {
          gameInstance.SendMessage('WebGLMobileInputReceiver', 'OnHtmlBlur', '');
        }
      });

      window.__unityMobileTmpInput = input;
    }

    input.type = isPassword ? 'password' : 'text';
    input.value = text || '';
    input.style.left = x + 'px';
    input.style.top = y + 'px';
    input.style.width = Math.max(width, 48) + 'px';
    input.style.height = Math.max(height, 32) + 'px';
    input.style.fontSize = Math.max(fontSize, 16) + 'px';
    input.style.display = 'block';

    setTimeout(function () {
      input.focus();
      input.select();
    }, 0);
  },

  WebGLMobileTMPInput_Hide: function () {
    var input = window.__unityMobileTmpInput;
    if (input) {
      input.blur();
      input.style.display = 'none';
    }
  }
});
