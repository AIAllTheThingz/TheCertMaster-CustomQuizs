(function (global) {
    function toCamelKey(key) {
        if (!key || typeof key !== 'string') return key;
        return key.length === 1 ? key.toLowerCase() : (key[0].toLowerCase() + key.slice(1));
    }

    function normalizeKeys(value) {
        if (Array.isArray(value)) {
            return value.map(normalizeKeys);
        }

        if (value && typeof value === 'object') {
            var normalized = {};
            for (var _i = 0, _a = Object.entries(value); _i < _a.length; _i++) {
                var entry = _a[_i];
                normalized[toCamelKey(entry[0])] = normalizeKeys(entry[1]);
            }
            return normalized;
        }

        return value;
    }

    async function readJsonNormalized(response) {
        var json = await response.json();
        return normalizeKeys(json);
    }

    function getValue(obj) {
        var keys = Array.prototype.slice.call(arguments, 1);
        for (var i = 0; i < keys.length; i++) {
            var key = keys[i];
            if (obj && obj[key] !== undefined && obj[key] !== null) {
                return obj[key];
            }
        }

        return '';
    }

    global.ApiJson = {
        toCamelKey: toCamelKey,
        normalizeKeys: normalizeKeys,
        readJsonNormalized: readJsonNormalized,
        getValue: getValue
    };
})(window);
