import { DetailsList, CheckboxVisibility, IColumn, Text } from "@fluentui/react";
import { Module } from "common/Interfaces";
import { copyAndSort } from "common/Utility";
import { TextLink } from "components/TextLink/TextLink";
import { useDataFileContext } from "context/DataFileContext";
import React, { useEffect, useRef, useState } from "react";
import { Col, Container, Row } from "react-grid-system";

const ModuleList: React.FC = () => {
  const [modules, setModules] = useState<Module[]>([]);
  const refModules = useRef(modules); //https://stackoverflow.com/a/64572688/670514
  const [colDef, setColDef] = useState<IColumn[]>([]);
  const refColDef = useRef(colDef); //https://stackoverflow.com/a/64572688/670514

  const { dataFile } = useDataFileContext();

  const updateModules = (newModules: Module[]) => {
    refModules.current = newModules;
    setModules(newModules);
  };

  const updateColumns = (newColumns: IColumn[]) => {
    refColDef.current = newColumns;
    setColDef(newColumns);
  };

  useEffect(() => {
    fetch(`/api/modulelist?filename=${dataFile}`)
      .then((res) => res.json())
      .then((data) => {
        updateModules(data);
        updateColumns(ModuleListColDef);
      });
  }, [dataFile]);

  const onColumnClick = (ev: React.MouseEvent<HTMLElement>, column: IColumn): void => {
    const newColumns: IColumn[] = refColDef.current.slice();
    const currColumn: IColumn = newColumns.filter((currCol) => column.key === currCol.key)[0];
    newColumns.forEach((newCol: IColumn) => {
      if (newCol === currColumn) {
        currColumn.isSortedDescending = !currColumn.isSortedDescending;
        currColumn.isSorted = true;
      } else {
        newCol.isSorted = false;
        newCol.isSortedDescending = true;
      }
    });
    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    const sortedModules = copyAndSort(refModules.current, currColumn.fieldName!, currColumn.isSortedDescending);
    updateModules(sortedModules);
    updateColumns(newColumns);
  };

  const ModuleListColDef: IColumn[] = [
    {
      key: "Module Path",
      name: "Module Path",
      fieldName: "modulePath",
      minWidth: 300,
      onColumnClick: onColumnClick,
    },
    {
      key: "Number of address occurrences in all stacks",
      name: "Number of address occurrences in all stacks",
      fieldName: "addrCount",
      minWidth: 300,
      onColumnClick: onColumnClick,
    },
  ];

  const renderItemColumn = (item?: Module, index?: number, column?: IColumn) => {
    if (column?.fieldName === "addrCount") {
      return (
        <TextLink
          onClick={() => {
            fetch(`/api/lookupsymbol?filename=${dataFile}&moduleIndex=${item?.id}`)
              .then((res) => res.json())
              .then((data) => {
                //! What are we supposed to do here??
                console.log(data);
              });
          }}
          content={item?.addrCount + "" || ""}
        />
      );
    } else {
      //? everything is optional..
      // eslint-disable-next-line @typescript-eslint/ban-ts-comment
      //@ts-ignore
      return item[column.fieldName];
    }
  };

  return (
    <Container>
      <Row>
        <Col>
          <Text variant={"xLarge"}>Module List</Text>
        </Col>
      </Row>
      <Row>
        <Col>
          <DetailsList
            checkboxVisibility={CheckboxVisibility.hidden}
            setKey={"key"}
            compact={true}
            items={modules}
            columns={colDef}
            onRenderItemColumn={renderItemColumn}
          />
        </Col>
      </Row>
    </Container>
  );
};

export { ModuleList };
