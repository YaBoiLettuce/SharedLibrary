namespace Shared.Http; 
 
 //Tipo de los elementos en la página.
public class PagedResult<T> 
{ 
  //Número total de elementos disponible en la colección completa.
  public int TotalCount { get; } 

  //Lista de elementos en la página actual.
  public List<T> Values { get; } 
 
  //Creación de un PagedResult Object con el total de elementos y la lista de valores. 
  public PagedResult(int totalCount, List<T> values) 
  { 
    TotalCount = totalCount; 
    Values = values; 
  } 
} 