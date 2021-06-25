import { Spinner, SpinnerSize } from "@fluentui/react/lib/Spinner";
import { IStackTokens, Stack } from "@fluentui/react/lib/Stack";
import React from "react";
import { useTranslation } from "react-i18next";

const stackTokens: IStackTokens = {
  childrenGap: 20,
  padding: 120,
};

const Loading: React.FC = () => {
  const { t } = useTranslation();

  return (
    <Stack tokens={stackTokens}>
      <Spinner label={t("loading")} size={SpinnerSize.large} />
    </Stack>
  );
};

export { Loading };
