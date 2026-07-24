//
//Source code is released under the MIT license.
//
//The MIT License (MIT)
//Copyright (c) 2014 Burtsev Alexey
//
//Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using Microsoft.AspNetCore.JsonPatch;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Core.Extensions
{
    public static class ObjectExtensions
    {
        /// <summary>
        /// Create the <see cref="JsonPatchDocument"/>
        /// </summary>
        /// <param name="originalObject">Object to change</param>
        /// <param name="modifiedObject">New object</param>
        /// <returns>The generated and filled in <see cref="JsonPatchDocument"/></returns>
        public static JsonPatchDocument CreatePatch(this object originalObject, object modifiedObject)
        {
            var original = JObject.FromObject(originalObject);
            var modified = JObject.FromObject(modifiedObject);

            var patch = new JsonPatchDocument();
            FillPatchForObject(original, modified, patch, "/");

            return patch;
        }

        /// <summary>
        /// Fill the <see cref="JsonPatchDocument"/>
        /// </summary>
        /// <param name="original">Object to change</param>
        /// <param name="modified">New Object</param>
        /// <param name="patch">Patch document being filled in</param>
        /// <param name="path">Object path</param>
        private static void FillPatchForObject(JObject original, JObject modified, JsonPatchDocument patch, string path)
        {
            var originalNames = original.Properties().Where(x => !x.Name.Equals("CreatedAt") 
                                                                && !x.Name.Equals("ModifiedAt")
                                                                && !x.Name.Equals("CreatedBy") 
                                                                && !x.Name.Equals("ModifiedBy")).Select(x => x.Name).ToArray();

            var modifiedNames = modified.Properties().Where(x => !x.Name.Equals("CreatedAt")
                                                                && !x.Name.Equals("ModifiedAt")
                                                                && !x.Name.Equals("CreatedBy")
                                                                && !x.Name.Equals("ModifiedBy")).Select(x => x.Name).ToArray();

            foreach(var removed in originalNames.Except(modifiedNames))
            {
                var prop = original.Property(removed);
                patch.Remove(path + prop.Name);
            }

            foreach(var added in modifiedNames.Except(originalNames))
            {
                var prop = modified.Property(added);
                patch.Add(path + prop.Name, prop.Value);
            }

            foreach(var intersects in originalNames.Intersect(modifiedNames))
            {
                var originalProp = original.Property(intersects);
                var modifiedProp = modified.Property(intersects);

                if (originalProp.Value.Type != modifiedProp.Value.Type)
                    patch.Replace(path + modifiedProp.Name, modifiedProp.Value);
                else if(!string.Equals(originalProp.Value.ToString(Newtonsoft.Json.Formatting.None), modifiedProp.Value.ToString(Newtonsoft.Json.Formatting.None)))
                {
                    if (originalProp.Value.Type == JTokenType.Object)
                        FillPatchForObject(originalProp.Value as JObject, modifiedProp.Value as JObject, patch, path + modifiedProp.Name + '/');
                    else
                        patch.Replace(path + modifiedProp.Name, modifiedProp.Value);
                }
            }
        }
    }
}
