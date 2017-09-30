#if NET40
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReadOnlyCollectionsExtensions.Wrappers {
    public class ArraySegmentWrapper<T>: IReadOnlyList<T> {
        private class ArraySegmentEnumerator<T>: IEnumerator<T> {
            private readonly ArraySegment<T> source;
            private readonly int lastIndexPlusOne;
            private int currentIndex;

            public ArraySegmentEnumerator(ArraySegment<T> source) {
                this.source = source;
                lastIndexPlusOne = source.Offset + source.Count;
                ((IEnumerator)this).Reset();
            }

            public bool MoveNext() {
                if (currentIndex >= lastIndexPlusOne)
                    return false;
                currentIndex++;
                return currentIndex < lastIndexPlusOne;
            }

            void IEnumerator.Reset() {
                currentIndex = source.Offset - 1;
            }

            public void Dispose() { }

            public T Current {
                get {
                    if (currentIndex < source.Offset)
                        throw new InvalidOperationException("Enumeration has not started");
                    if (currentIndex >= lastIndexPlusOne)
                        throw new InvalidOperationException("Enumeration ended");
                    return source.Array[currentIndex];
                }
            }

            object IEnumerator.Current {
                get {
                    return (object)((IEnumerator<T>)this).Current;
                }
            }
        }

        private readonly ArraySegment<T> source;

        public ArraySegmentWrapper(ArraySegment<T> source) {
            this.source = source;
        }

        public IEnumerator<T> GetEnumerator() {
            return new ArraySegmentEnumerator<T>(source);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return new ArraySegmentEnumerator<T>(source);
        }

        public int Count {
            get { return source.Count; }
        }

        public T this[int index] {
            get { return source.Array[source.Offset + index]; }
        }
    }
}
#endif