using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using Excel;
using System.Data;
using System;
using System.Text;

public class ExcelTool
{
    /// <summary>
    /// 字段名称所在行
    /// </summary>
    public static int t1 = 0;
    /// <summary>
    /// 字段类型所在行
    /// </summary>
    public static int t2 = 1;
    /// <summary>
    /// 特殊信息所在行
    /// </summary>
    public static int t3 = 2;
    /// <summary>
    /// 非数据的总行数，表行数减去此值就是所有数据行数
    /// </summary>
    public static int t4 = 4;


    /// <summary>
    /// 读取的excel配置信息的文件路径
    /// </summary>
    public static string ExcelDataPath = Application.dataPath + "/Script/Data/Editor/Excel/";
    /// <summary>
    /// 数据结构类存放路径
    /// </summary>
    public static string eInfoClass = Application.dataPath + "/Script/Data/Info/";
    /// <summary>
    /// 数据容器类存放路径，一个excel的不同子表公用一个容器类
    /// </summary>
    public static string eInfoCollection = Application.dataPath + "/Script/Data/Collection/";


    [MenuItem("自定义工具/开发工具/数据/加载Excel数据")]
    private static void CreateExcelInfo()
    { 
        //找到文件夹信息
        DirectoryInfo directory = Directory.CreateDirectory(ExcelDataPath);
        //获取包含此文件夹的全部文件的数组
        FileInfo[] files = directory.GetFiles();
        //申明表格容器
        DataTableCollection tableInfoCollection;
        if (!Directory.Exists(eInfoCollection))
            Directory.CreateDirectory(eInfoCollection);

        foreach (FileInfo file in files)
        {
            //只读取Excel相关文件
            if (file.Extension!= ".xlsx"&&
                file.Extension != ".xls")
            {
                continue;
            }
            //容器类名为文件名
            string nameCollection = file.Name.Substring(0, file.Name.Length - file.Extension.Length);
            //类结构
            string strData = "using System.Collections.Generic;\n";
            strData += "public class " + nameCollection + ":BaseCollection\n{\n";
            //以文件流的形式让excel相关类能够用于使用和读取excel
            using (FileStream fs=file.Open(FileMode.Open,FileAccess.Read))
            {
                IExcelDataReader i1 =ExcelReaderFactory.CreateOpenXmlReader(fs);
                tableInfoCollection = i1.AsDataSet().Tables;//获取表格容器信息

                fs.Close();
            }

            for (int sheetIndex = 0; sheetIndex < tableInfoCollection.Count; sheetIndex++)
            {
                DataTable table = tableInfoCollection[sheetIndex];
                //构建数据结构类
                SetInfoCalss(table);
                //构建数据容器类
                strData = GetInfoString(table, strData, sheetIndex);
                //生成二进制文件
                BinaryInfoSave(table);
            }

            //存入容器类文件
            strData += "}";
            File.WriteAllText(eInfoCollection+nameCollection+".cs", strData);
        }

        //刷新Project窗口
        AssetDatabase.Refresh();
    }

    private static void SetInfoCalss(DataTable table)
    {
        //读取字段名称和字段类型
        DataRow row1 = table.Rows[t1];
        DataRow row2 = table.Rows[t2];
        //自动生成文件路径
        if(!Directory.Exists(eInfoClass))
            Directory.CreateDirectory(eInfoClass);

        string str = "public class " + table.TableName + "\n{\n";

        for (int i = 0;i < table.Columns.Count;i++)
        {
            // 根据列索引 i 添加 FieldOrder 特性
            str += $"    [FieldOrder({i})] public {row2[i]} {row1[i]};\n";
        }

        str += "\n}";
        //存入拼接好的字符串，文件名为类名，后缀.cs
        File.WriteAllText(eInfoClass+table.TableName+".cs", str);
    }

    private static string GetInfoString(DataTable table,string st,int sheetIndex)
    {
        // 注意：string 虽是引用类型，但不可变且按值传递，这里对 st 的 += 实际是生成新字符串，
        // 只会修改方法内的拷贝，外部的 strData 不会发生任何变化

        DataRow row2 = table.Rows[t2];
        int i = GetKeyValue(table);
        if (i < 0)
            Debug.LogError("未找到主键位置，请检查表"+table.TableName);
        st += "    [FieldOrder(" + sheetIndex + ")]\n";
        st += "     public Dictionary< " + row2[i] + "," + table.TableName + "> "+ table.TableName+ "Dic =" +
                "new Dictionary<" + row2[i] + "," + table.TableName + ">();\n";
        return st;
    }

    private static void BinaryInfoSave(DataTable table)
    {
        if(!Directory.Exists(DataAndInitMgr.binaryDataPath))
            Directory.CreateDirectory (DataAndInitMgr.binaryDataPath);
        using(FileStream fs=new FileStream(DataAndInitMgr.binaryDataPath +table.TableName+".bitto", FileMode.OpenOrCreate, FileAccess.Write))
        {
            //存入数据总行数
            fs.Write(BitConverter.GetBytes(table.Rows.Count - t4), 0, 4);
            //存入主键键名（因为名称唯一，通过反射获取类型对应的值，用于存入字典）
            string keyName = table.Rows[t1][GetKeyValue(table)].ToString();
            fs.Write(BitConverter.GetBytes(keyName.Length), 0, 4);
            fs.Write(Encoding.UTF8.GetBytes(keyName), 0, keyName.Length);

            //遍历所有数据行，直接存入内容
            //先得到字段类型
            DataRow drow = table.Rows[t2];
            DataRow row;
            //注意：j的起始位置为t4
            for (int j=t4;j<table.Rows.Count;j++)
            {
                //得到具体行
                row = table.Rows[j];
                for(int i = 0; i < table.Columns.Count; i++)
                {
                    try
                    {
                        switch (drow[i].ToString())
                        {

                            case "int":
                                fs.Write(BitConverter.GetBytes(int.Parse(row[i].ToString())), 0, 4);
                                break;
                            case "float":
                                fs.Write(BitConverter.GetBytes(float.Parse(row[i].ToString())), 0, 4);
                                break;
                            case "string":
                                byte[] bytes = Encoding.UTF8.GetBytes(row[i].ToString());
                                fs.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                                fs.Write(bytes, 0, bytes.Length);
                                break;
                            case "bool":
                                fs.Write(BitConverter.GetBytes(bool.Parse(row[i].ToString())), 0, 1);
                                break;
                            default:
                                Debug.LogError($"字段类型非法 表:{table.TableName} 列:{i + 1} 类型:[{drow[i].ToString()}]");
                                break;
                        }
                    }
                    catch
                    {
                        Debug.LogError($"第{j+1}行，第{i+1}列，数据读取报错，请检查类型与值是否配对");
                    }

                }
            }
            fs.Close();
        }
    }

    /// <summary>
    /// 获取主键索引
    /// </summary>
    private static int GetKeyValue(DataTable tb)
    {
        DataRow row = tb.Rows[t3];
        for (int i = 0; i < tb.Columns.Count; i++)
        {
            if (row[i].ToString() == "key")
                return i;
        }
        //返回-1表示没有找到主键位置
        return -1;
    }
}
