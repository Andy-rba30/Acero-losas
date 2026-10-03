using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arba.Comun;
using Autodesk.Revit.UI;

namespace SlabRebar
{
    /// <summary>
    /// Entrada de la aplicacion de cinta para SlabRebar en Revit.
    /// Anade el boton "Losas" al desplegable "Acero" del panel "Acero" en la pestana "ARBA".
    /// La pestana, los paneles y el desplegable los gestiona la clase comun <see cref="ArbaRibbon"/>
    /// (ARBA-comun): todos los add-ins ARBA escriben en la misma cinta sin importar cual cargue primero.
    /// </summary>
    public class RibbonApp : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                ArbaRibbon.Ensure(app);

                string assembly = Assembly.GetExecutingAssembly().Location;
                var data = new PushButtonData("ARBA_Acero_Losas", "Losas", assembly, typeof(ArmarLosaCommand).FullName)
                {
                    ToolTip = "Genera el armado de losas aligeradas (viguetas, bastones y acero de temperatura) y macizas (mallas inferior y superior)",
                    LongDescription = "Selecciona una o varias losas (suelos estructurales) y pulsa el boton. Se abre la ventana " +
                                      "para elegir el tipo de losa, la direccion de las viguetas o de las barras principales, los " +
                                      "tipos de barra, separaciones, bastones y acero de temperatura, con un esquema en planta y " +
                                      "de la seccion. Si no hay nada seleccionado, el comando pide que elijas las losas. " +
                                      "Contrato ARBA " + ArbaContract.Version + ".",
                    LargeImage = IconLosas(32),
                    Image = IconLosas(16)
                };

                ArbaRibbon.AddAcero(app, data);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ARBA", "No se pudo anadir el boton Losas a la cinta: " + ex.Message +
                                "\nEl comando sigue disponible en Complementos > Herramientas externas.");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        /// <summary>
        /// Icono del boton Losas: seccion transversal de una losa aligerada (losa superior,
        /// dos ladrillos, una vigueta con su barra inferior y su baston, y el acero de
        /// temperatura como una raya en la losa superior).
        /// </summary>
        public static BitmapSource IconLosas(int size)
        {
            double s = size / 32.0;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var concrete = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1.2 * s);
                var brick = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x2E));
                var brickEdge = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x5A, 0x1A)), 0.9 * s);
                var bar = new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E));
                var top = new SolidColorBrush(Color.FromRgb(0xD9, 0x6C, 0x2A));
                var temp = new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x3E, 0x9D)), 1.4 * s);

                // hormigon: losa completa
                dc.DrawRectangle(concrete, edge, new Rect(1.5 * s, 7 * s, 29 * s, 18 * s));
                // ladrillos (huecos) a los dos lados de la vigueta central
                dc.DrawRectangle(brick, brickEdge, new Rect(2.5 * s, 13 * s, 9.5 * s, 11 * s));
                dc.DrawRectangle(brick, brickEdge, new Rect(20 * s, 13 * s, 9.5 * s, 11 * s));
                // acero de temperatura en la losa superior
                dc.DrawLine(temp, new Point(2.5 * s, 10.5 * s), new Point(29.5 * s, 10.5 * s));
                // barra inferior de la vigueta y baston superior
                dc.DrawEllipse(bar, null, new Point(16 * s, 22 * s), 1.9 * s, 1.9 * s);
                dc.DrawEllipse(top, null, new Point(16 * s, 9.3 * s), 1.6 * s, 1.6 * s);
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
