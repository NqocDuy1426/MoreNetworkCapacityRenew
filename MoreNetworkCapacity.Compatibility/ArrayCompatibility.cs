using System;
using System.Reflection;

namespace MoreNetworkCapacity.Compatibility {

internal static class ArrayCompatibility
{
	internal static FieldInfo FindField(Type type, string name)
	{
		if ((object)type == null || string.IsNullOrEmpty(name))
		{
			return null;
		}
		return type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
	}

	internal static FieldInfo FindBackingField(Type type, string propertyName)
	{
		if ((object)type == null || string.IsNullOrEmpty(propertyName))
		{
			return null;
		}
		return FindField(type, "<" + propertyName + ">k__BackingField");
	}

	internal static object GetStaticInstance(Type type, string propertyName, string fieldName, string label)
	{
		try
		{
			if ((object)type == null)
			{
				return null;
			}
			if (!string.IsNullOrEmpty(propertyName))
			{
				PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if ((object)property != null)
				{
					return property.GetValue(null, null);
				}
			}
			if (!string.IsNullOrEmpty(fieldName))
			{
				FieldInfo fieldInfo = FindField(type, fieldName);
				if ((object)fieldInfo != null)
				{
					return fieldInfo.GetValue(null);
				}
			}
		}
		catch (Exception ex)
		{
			Logging.Error("Could not access " + label + " instance: " + ex);
		}
		return null;
	}

	internal static void ExpandDirect(FieldInfo field, object target, int oldExpectedLength, int newLength, IndexedElementFactory factory, bool createIfNull, string label)
	{
		if ((object)field == null)
		{
			Logging.Warning("Could not find compatibility array " + label + ".");
			return;
		}
		try
		{
			Type elementType = field.FieldType.GetElementType();
			if ((object)elementType == null)
			{
				Logging.Warning("Compatibility member is not an array: " + label + ".");
			}
			else if (!(field.GetValue(target) is Array array))
			{
				if (!createIfNull)
				{
					Logging.Info("Compatibility array is not initialized yet; skipping " + label + ".");
					return;
				}
				Array array2 = Array.CreateInstance(elementType, newLength);
				InitializeRange(array2, 0, newLength, factory);
				field.SetValue(target, array2);
				Logging.Info("Created compatibility array " + label + " at length " + newLength + ".");
			}
			else if (array.Length != newLength)
			{
				if (oldExpectedLength > 0 && array.Length != oldExpectedLength)
				{
					Logging.Error("Compatibility array " + label + " has an unexpected length " + array.Length + "; expected " + oldExpectedLength + " or " + newLength + ".");
				}
				else
				{
					Array array3 = Array.CreateInstance(elementType, newLength);
					Array.Copy(array, array3, array.Length);
					InitializeRange(array3, oldExpectedLength, newLength, factory);
					field.SetValue(target, array3);
					Logging.Info("Expanded compatibility array " + label + ": " + array.Length + " -> " + newLength + ".");
				}
			}
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to expand compatibility array " + label + ": " + ex);
		}
	}

	internal static void ExpandSplit(FieldInfo field, object target, int oldItemCapacity, int newItemCapacity, SplitElementFactory factory, string label)
	{
		if ((object)field == null)
		{
			Logging.Warning("Could not find split compatibility array " + label + ".");
			return;
		}
		try
		{
			Type elementType = field.FieldType.GetElementType();
			if ((object)elementType == null)
			{
				Logging.Warning("Compatibility member is not an array: " + label + ".");
				return;
			}
			int num = oldItemCapacity * 2;
			int num2 = newItemCapacity * 2;
			if (!(field.GetValue(target) is Array array))
			{
				Logging.Info("Split compatibility array is not initialized yet; skipping " + label + ".");
			}
			else
			{
				if (array.Length == num2)
				{
					return;
				}
				if (array.Length != num)
				{
					Logging.Error("Split compatibility array " + label + " has an unexpected length " + array.Length + "; expected " + num + " or " + num2 + ".");
					return;
				}
				Array array2 = Array.CreateInstance(elementType, num2);
				Array.Copy(array, 0, array2, 0, oldItemCapacity);
				Array.Copy(array, oldItemCapacity, array2, newItemCapacity, oldItemCapacity);
				for (int i = oldItemCapacity; i < newItemCapacity; i++)
				{
					array2.SetValue(CreateSplitElement(factory, i, startNode: true), i);
					array2.SetValue(CreateSplitElement(factory, i, startNode: false), newItemCapacity + i);
				}
				field.SetValue(target, array2);
				Logging.Info("Migrated and expanded split compatibility array " + label + ": " + array.Length + " -> " + num2 + " (offset " + oldItemCapacity + " -> " + newItemCapacity + ").");
			}
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to migrate split compatibility array " + label + ": " + ex);
		}
	}

	private static void InitializeRange(Array array, int start, int end, IndexedElementFactory factory)
	{
		if (factory != null)
		{
			for (int i = start; i < end; i++)
			{
				array.SetValue(factory(i), i);
			}
		}
	}

	private static object CreateSplitElement(SplitElementFactory factory, int index, bool startNode)
	{
		return factory?.Invoke(index, startNode);
	}
}

}
