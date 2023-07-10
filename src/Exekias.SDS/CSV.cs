using Exekias.Core;
using Microsoft.Research.Science.Data;
using Microsoft.Research.Science.Data.Factory;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Exekias.SDS;
using Microsoft.Research.Science.Data.CSV;

namespace Exekias.DataImporterPart
{
    public class CSV : DataImporterPartBase
    {
        override public bool CanImport(string path)
        {
            return "csv" == DataSetFactory.GetProviderNameByExtention(path);
        }

        override public ValueTask<bool> Import(string localPath, DataSet target, Dictionary<string, object> metadata)
        {
            if (localPath == null) throw new ArgumentNullException(nameof(localPath));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            using (var temp = DataSet.Open("msds:memory"))
            {
                var uri = new CsvUri()
                {
                    FileName = localPath,
                    OpenMode = ResourceOpenMode.ReadOnly
                };
                using (var input = DataSet.Open(uri))
                {
                    DataSet.CopyAll(input, temp);
                }
                foreach (var v in temp.Variables)
                {
                    if (v.Metadata.ContainsKey(v.Metadata.KeyForMissingValue)
                        && null == v.MissingValue
                        && typeof(string) == v.TypeOfData)
                    { v.MissingValue = ""; }
                }
                DataSet.CopyAll(temp, target);
            }
            if (metadata.ContainsKey(ImportStoreBase.DatasetReuseKey)
                && metadata.GetBoolean(ImportStoreBase.DatasetReuseKey) == false)
            {
                return new ValueTask<bool>(false);
            }
            metadata[ImportStoreBase.DatasetReuseKey] = false;
            return new ValueTask<bool>(true);
        }
    }
}
