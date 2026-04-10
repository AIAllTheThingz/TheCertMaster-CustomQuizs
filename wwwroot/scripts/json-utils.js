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
            const normalized = {};
            for (const [key, item] of Object.entries(value)) {
                normalized[toCamelKey(key)] = normalizeKeys(item);
            }

            return normalized;
        }

        return value;
    }

    async function readJsonNormalized(response) {
        const json = await response.json();
        return normalizeKeys(json);
    }

    function getValue(obj) {
        const keys = Array.prototype.slice.call(arguments, 1);
        for (const key of keys) {
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
