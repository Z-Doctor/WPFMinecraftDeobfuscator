using System;
using System.Collections.Generic;

public class BinaryTree<K, V> where K : IComparable<K>, IEquatable<K> {

    public int Count { get; private set; }
    public Dictionary<K, Node> Head { get; } = new Dictionary<K, Node>();

    private readonly Dictionary<IEnumerable<K>, V> keyValuePairs = new Dictionary<IEnumerable<K>, V>();
    public Dictionary<IEnumerable<K>, V> KeyValuePairs { get => new Dictionary<IEnumerable<K>, V>(keyValuePairs); }

    public void Clear() {
        Head.Clear();
        Count = 0;
        keyValuePairs.Clear();
    }

    public Node this[K key] {
        get {
            if (Head.TryGetValue(key, out Node node))
                return node;
            return null;
        }
    }

    public V this[IEnumerable<K> keys] {
        get => keyValuePairs[keys];
    }

    public void AddOrUpdate(IEnumerable<K> keys, V value) {
        bool addedEntry = false;
        Node current = null;

        foreach (var key in keys) {
            if (current is null) {
                current = this[key];
                if (current is null) {
                    current = new Node(key);
                    Head[key] = current;
                    addedEntry = true;
                }
            } else {
                Node node = current[key];
                if (node is null) {
                    node = new Node(key, current);
                    addedEntry = true;
                }
                current = node;
            }
        }
        if (current is null)
            return;
        if (addedEntry)
            Count++;
        current.Keys = keys;
        current.Value = value;
        keyValuePairs[keys] = value;
    }

    public class Node {
        public IEnumerable<K> Keys { get; set; }
        public K Key { get; }
        public V Value { get; set; }
        private Dictionary<K, Node> ChildNodes { get; } = new Dictionary<K, Node>();

        public Node this[K key] {
            get {
                if (ChildNodes.TryGetValue(key, out Node node))
                    return node;
                return null;
            }
        }

        public Node(K key, Node parent = null) {
            Key = key;
            parent?.AddChild(key, this);
        }

        public Node(K key, V value, Node parent = null) {
            Key = key;
            Value = value;
            parent?.AddChild(key, this);
        }

        public void AddChild(K key, Node node) {
            ChildNodes.Add(key, node);
        }

        public override bool Equals(object obj) {
            if (obj is null)
                return false;

            if (obj is K)
                return Equals((K)obj);
            if (obj is Node)
                return Equals((Node)obj);
            return base.Equals(obj);
        }

        public bool Equals(K key) {
            return Key.Equals(key);
        }

        public bool Equals(Node node) {
            return Key.Equals(node.Key);
        }

        public override int GetHashCode() {
            return Key.GetHashCode();
        }
    }

}