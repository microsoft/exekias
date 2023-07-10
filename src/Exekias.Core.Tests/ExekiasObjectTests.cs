using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Exekias.Core.Tests
{
    public class ExekiasObjectTests
    {
        [Fact]
        public void Default_property_values()
        {
            var observed = new ExekiasObject();
            Assert.Null(observed.Run);
            Assert.Null(observed.Path);
            Assert.Equal(DateTimeOffset.MinValue, observed.LastWriteTime);
            Assert.Equal(ExekiasObjectType.Data, observed.Type);
            Assert.Empty(observed.Meta);
            Assert.Equal(5, typeof(ExekiasObject).GetProperties().Length);
        }

        string Serialize(ExekiasObject obj)
        {
            return JsonSerializer.Serialize(obj,
                new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        ExekiasObject Deserialize(string json)
        {
            return JsonSerializer.Deserialize<ExekiasObject>(json,
                new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }


        [Fact]
        public void Default_JSON_serialization()
        {
            var observed = Serialize(new ExekiasObject());
            Assert.Equal(
                "{\"run\":null,\"path\":null,\"lastWriteTime\":\"0001-01-01T00:00:00+00:00\",\"type\":0}",
                observed);
        }

        [Fact]
        public void Deserialize_from_sample_JSON()
        {
            var test = @"{
""run"": ""a"",
""path"": """",
""lastWriteTime"": ""2020-05-09T14:09:00+02:00"",
""type"": 1,
""params"": {""author"": ""vassilyl""},
""startTime"": ""20200509-140900"",
""number"": 0,
""reuseDataSet"": false,
""variables"": {""SDS"": [""one"", ""two""]}
}";
            var observed = Deserialize(test);
            Assert.Equal("a", observed.Run);
            Assert.Equal(string.Empty, observed.Path);
            Assert.Equal(new DateTimeOffset(2020, 5, 9, 12, 9, 0, TimeSpan.Zero), observed.LastWriteTime);
            Assert.Equal(ExekiasObjectType.Metadata, observed.Type);
            Assert.Equal(5, observed.Meta.Count);
            Assert.Equal("20200509-140900", observed.Meta.GetString("startTime"));
            var paramsElt = (JsonElement)observed.Meta["params"];
            Assert.Single(paramsElt.EnumerateObject());
            Assert.Equal("vassilyl", paramsElt.GetProperty("author").GetString());
            Assert.Equal(0, observed.Meta.GetInt32("number"));
            Assert.False(observed.Meta.GetBoolean("reuseDataSet"));
            Assert.Equal(new Dictionary<string, string[]>(){ { "SDS", new string[] { "one", "two" }} }, observed.GetVariables());
        }

        [Fact]
        public void Meta_string_roundtrip()
        {
            var test = new ExekiasObject();
            test.Meta["a"] = "b";
            test.Meta["EMPTY"] = string.Empty;
            test.Meta["null"] = null;
            test.Meta["c"] = 1;
            Assert.Null(test.Meta.GetString("null"));
            Assert.Empty(test.Meta.GetString("EMPTY"));
            Assert.Equal("b", test.Meta.GetString("a"));
            Assert.Throws<InvalidOperationException>(() => test.Meta.GetString("c"));
            Assert.Throws<KeyNotFoundException>(() => test.Meta.GetString("d"));
            var text = Serialize(test);
            var observed = Deserialize(text);
            Assert.Null(observed.Meta.GetString("null"));
            Assert.Empty(observed.Meta.GetString("EMPTY"));
            Assert.Equal("b", observed.Meta.GetString("a"));
            Assert.Throws<InvalidOperationException>(() => observed.Meta.GetString("c"));
        }

        [Fact]
        public void Meta_bool_roundtrip()
        {
            var test = new ExekiasObject();
            test.Meta["a"] = "b";
            test.Meta["t"] = true;
            test.Meta["f"] = false;
            test.Meta["null"] = null;
            Assert.True(test.Meta.GetBoolean("t"));
            Assert.False(test.Meta.GetBoolean("f"));
            Assert.Throws<InvalidOperationException>(() => test.Meta.GetBoolean("a"));
            Assert.Throws<NullReferenceException>(() => test.Meta.GetBoolean("null"));
            Assert.Throws<KeyNotFoundException>(() => test.Meta.GetBoolean("c"));
            var text = Serialize(test);
            var observed = Deserialize(text);
            Assert.True(observed.Meta.GetBoolean("t"));
            Assert.False(observed.Meta.GetBoolean("f"));
            Assert.Throws<InvalidOperationException>(() => observed.Meta.GetBoolean("a"));
            Assert.Throws<NullReferenceException>(() => observed.Meta.GetBoolean("null"));
            Assert.Throws<KeyNotFoundException>(() => observed.Meta.GetBoolean("c"));
        }

        [Fact]
        public void Meta_int_roundtrip()
        {
            var test = new ExekiasObject();
            test.Meta["z"] = 0;
            test.Meta["n"] = int.MinValue;
            test.Meta["p"] = int.MaxValue;
            test.Meta["a"] = "b";
            test.Meta["null"] = null;
            Assert.Equal(0, test.Meta.GetInt32("z"));
            Assert.Equal(int.MinValue, test.Meta.GetInt32("n"));
            Assert.Equal(int.MaxValue, test.Meta.GetInt32("p"));
            Assert.Throws<InvalidOperationException>(() => test.Meta.GetInt32("a"));
            Assert.Throws<NullReferenceException>(() => test.Meta.GetInt32("null"));
            Assert.Throws<KeyNotFoundException>(() => test.Meta.GetInt32("c"));
            var text = Serialize(test);
            var observed = Deserialize(text);
            Assert.Equal(0, observed.Meta.GetInt32("z"));
            Assert.Equal(int.MinValue, observed.Meta.GetInt32("n"));
            Assert.Equal(int.MaxValue, observed.Meta.GetInt32("p"));
            Assert.Throws<InvalidOperationException>(() => observed.Meta.GetInt32("a"));
            Assert.Throws<NullReferenceException>(() => observed.Meta.GetInt32("null"));
            Assert.Throws<KeyNotFoundException>(() => observed.Meta.GetInt32("c"));
        }

        [Fact]
        public void Meta_string_array_roundtrip()
        {
            var test = new ExekiasObject();
            test.Meta["empty"] = new string[0];
            test.Meta["vars"] = new string[] { "one", "two" };
            test.Meta["a"] = "b";
            test.Meta["null"] = null;
            Assert.Empty(test.Meta.GetStringArray("empty"));
            Assert.Equal(new string[] { "one", "two" }, test.Meta.GetStringArray("vars"));
            Assert.Throws<InvalidOperationException>(() => test.Meta.GetStringArray("a"));
            Assert.Throws<NullReferenceException>(() => test.Meta.GetStringArray("null"));
            Assert.Throws<KeyNotFoundException>(() => test.Meta.GetStringArray("c"));
            var text = Serialize(test);
            var observed = Deserialize(text);
            Assert.Empty(observed.Meta.GetStringArray("empty"));
            Assert.Equal(new string[] { "one", "two" }, observed.Meta.GetStringArray("vars"));
            Assert.Throws<InvalidOperationException>(() => observed.Meta.GetStringArray("a"));
            Assert.Throws<NullReferenceException>(() => observed.Meta.GetStringArray("null"));
            Assert.Throws<KeyNotFoundException>(() => observed.Meta.GetStringArray("c"));
        }

        [Fact]
        public void Meta_dictionary_roundtrip()
        {
            var test = new ExekiasObject();
            test.Meta["empty"] = new Dictionary<string, object>();
            test.Meta["vars"] = new Dictionary<string, object>(){
                {"SDS", new string[] { "one", "two" } } };
            test.Meta["a"] = "b";
            test.Meta["null"] = null;
            Assert.Empty(test.Meta.GetDictionary("empty"));
            Assert.Equal(new string[] { "one", "two" }, 
                test.Meta.GetDictionary("vars").GetStringArray("SDS"));
            Assert.Throws<InvalidOperationException>(() => test.Meta.GetDictionary("a"));
            Assert.Throws<NullReferenceException>(() => test.Meta.GetDictionary("null"));
            Assert.Throws<KeyNotFoundException>(() => test.Meta.GetDictionary("c"));
            var text = Serialize(test);
            var observed = Deserialize(text);
            Assert.Empty(observed.Meta.GetDictionary("empty"));
            Assert.Equal(new string[] { "one", "two" }, 
                observed.Meta.GetDictionary("vars").GetStringArray("SDS"));
            Assert.Throws<InvalidOperationException>(() => observed.Meta.GetDictionary("a"));
            Assert.Throws<NullReferenceException>(() => observed.Meta.GetDictionary("null"));
            Assert.Throws<KeyNotFoundException>(() => observed.Meta.GetDictionary("c"));
        }
    }
}
