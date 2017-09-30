using System;
using System.Dynamic;

namespace nucs.JsonSettings {
    public sealed class DynamicSettingsBag : DynamicObject, IDisposable {

        public SettingsBag AsBag() {
            return __bag;
        }

        public DynamicSettingsBag(SettingsBag bag) {
            __bag = bag;
        }

        private SettingsBag __bag { get; set; }

        public void Dispose() {
            __bag = null;
        }

        // Get the property value.
        public override bool TryGetMember(GetMemberBinder binder, out object result) {
            result = __bag[binder.Name];
            return true;
        }

        // Set the property value.
        public override bool TrySetMember(SetMemberBinder binder, object value) {
            __bag[binder.Name] = value;
            return true;
        }

        // Set the property value by index.
        public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object value) {
            var index = indexes[0] as string;
            if (string.IsNullOrEmpty(index))
                return false;
            __bag[index] = value;
            
            return true;
        }

        // Get the property value by index.
        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result) {
            var index = indexes[0] as string;
            if (string.IsNullOrEmpty(index)) {
                result = null;
                return false;
            }
            result = __bag[index];
            return true;
        }
    }
}