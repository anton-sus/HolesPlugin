using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HolesPlugin
{
    [Transaction(TransactionMode.Manual)]
    public class AddHole : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document arDoc = commandData.Application.ActiveUIDocument.Document;
            Document ovDoc = arDoc.Application.Documents.OfType<Document>().Where(x=>x.Title.Contains("ОВ")).FirstOrDefault();
            if (ovDoc == null)
            {
                TaskDialog.Show("Ошибка", "Файл не найден");
                return Result.Cancelled;
            }

            FamilySymbol familySymbol = new FilteredElementCollector(arDoc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .OfType<FamilySymbol>()
                .Where(x => x.FamilyName.Equals("Отверстия"))
                .FirstOrDefault();
            if (familySymbol == null)
            {
                TaskDialog.Show("Ошибка", "Семейство \"Отверстия\" не найдено");
                return Result.Cancelled;
            }

            List<Element> ducts = new FilteredElementCollector(ovDoc)
                .OfClass(typeof(Duct))
                .OfType<Duct>()
                .Cast<Element>()
                .ToList(); 
            
            List<Element> pipes = new FilteredElementCollector(ovDoc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .ToList();
           
            List<Element> elementsList = pipes;
            elementsList.AddRange(ducts);

            View3D view3D = new FilteredElementCollector(arDoc)
                .OfClass(typeof(View3D))
                .OfType<View3D>()
                .Where(x=>!x.IsTemplate)
                .FirstOrDefault(); 
            if (view3D == null)
            {
                TaskDialog.Show("Ошибка", "3D вид не найден");
                return Result.Cancelled;
            }

            ReferenceIntersector referenceIntersector = new ReferenceIntersector(new ElementClassFilter(typeof(Wall)), FindReferenceTarget.Element, view3D) ;

            Transaction tx = new Transaction(arDoc);
            tx.Start("create holes");
           
            foreach (Element e in elementsList)
            {
                Line curve = (e.Location as LocationCurve).Curve as Line;
                XYZ point = curve.GetEndPoint(0);
                XYZ direction = curve.Direction;

                List<ReferenceWithContext> intersections = referenceIntersector.Find(point, direction)
                     .Where(x => x.Proximity <= curve.Length)
                     .Distinct(new ReferenceWithContextElementEqualityComparer())
                     .ToList();
                foreach (ReferenceWithContext refer in intersections)
                {
                    double proximity = refer.Proximity;
                    Reference reference = refer.GetReference();
                    Wall wall = arDoc.GetElement(reference.ElementId) as Wall;
                    Level level = arDoc.GetElement(wall.LevelId) as Level;
                    XYZ pointHole = point + direction * proximity;

                    FamilyInstance hole = arDoc.Create.NewFamilyInstance(pointHole, familySymbol, wall, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                    Parameter width = hole.LookupParameter("Ширина");
                    Parameter height = hole.LookupParameter("Высота");
                    
                    Parameter diamPar = e.LookupParameter("Диаметр");
                   double o = UnitUtils.ConvertFromInternalUnits(diamPar.AsDouble(), UnitTypeId.Meters);

                    width.Set(o*3.5);
                    height.Set(o*3.5);
                }
            }
            tx.Commit();
            return Result.Succeeded;
        }
        public class ReferenceWithContextElementEqualityComparer : IEqualityComparer<ReferenceWithContext>
        {
            public bool Equals(ReferenceWithContext x, ReferenceWithContext y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(null, x)) return false;
                if (ReferenceEquals(null, y)) return false;

                var xReference = x.GetReference();

                var yReference = y.GetReference();

                return xReference.LinkedElementId == yReference.LinkedElementId
                           && xReference.ElementId == yReference.ElementId;
            }

            public int GetHashCode(ReferenceWithContext obj)
            {
                var reference = obj.GetReference();

                unchecked
                {
                    return (reference.LinkedElementId.GetHashCode() * 397) ^ reference.ElementId.GetHashCode();
                }
            }
        }
    }
}
