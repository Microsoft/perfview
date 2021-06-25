import { RouteComponentProps, useHistory } from "react-router-dom";
import React, { useEffect, useState } from "react";
import base64url from "base64url";
import { useDataFileContext } from "context/DataFileContext";
import { CheckboxVisibility, DetailsList, IColumn } from "@fluentui/react";
import { ProcessChooserColDef } from "./ProcessChooserColDef";
import { useRouteKeyContext } from "context/RouteContext";
import { TextLink } from "components/TextLink/TextLink";

interface MatchParams {
  stackType: string;
  stackTypeName: string;
}

export interface Props extends RouteComponentProps<MatchParams> {}

interface Process {
  name: string;
  id: number;
  cpumSec: number;
}

const ProcessChooser: React.FC<Props> = (props) => {
  const { match } = props;
  const { dataFile } = useDataFileContext();
  const history = useHistory();
  const { setRouteKey } = useRouteKeyContext();
  const [processes, setProcesses] = useState<Process[]>([]);
  useEffect(() => {
    fetch("/api/processchooser?filename=" + dataFile + "&stacktype=" + match.params.stackType)
      .then((res) => res.json())
      .then((data) => {
        setProcesses(data);
      });
  }, [dataFile, match.params.stackType]);

  const renderItemColumn = (item?: Process, index?: number, column?: IColumn) => {
    if (column?.fieldName === "name") {
      return (
        <TextLink
          onClick={() => {
            const routeKey = base64url.encode(
              JSON.stringify({
                a: dataFile,
                b: match.params.stackType,
                c: item?.id,
                d: "",
                e: "",
                f: "",
                g: "",
                h: item?.id === -1 ? "" : "Process% " + item?.name,
                i: "",
                j: "",
                k: "",
                l: match.params.stackTypeName,
              })
            );
            setRouteKey(routeKey);
            const hotspotUrl = `/ui/stackviewer/hotspots/${routeKey}`;
            history.push(hotspotUrl);
          }}
          content={item?.name || ""}
        ></TextLink>
      );
    } else {
      //? everything is optional..
      // eslint-disable-next-line @typescript-eslint/ban-ts-comment
      //@ts-ignore
      return item[column.fieldName];
    }
  };

  return (
    <div>
      <h4>Event {base64url.decode(match.params.stackTypeName)} &raquo; Choose Process</h4>
      <DetailsList
        checkboxVisibility={CheckboxVisibility.hidden}
        setKey={"key"}
        compact={true}
        items={processes}
        columns={ProcessChooserColDef}
        onRenderItemColumn={renderItemColumn}
      />
    </div>
  );
};

export { ProcessChooser };
